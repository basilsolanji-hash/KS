using KnitErp.Application.Access;
using KnitErp.Application.Authentication;
using KnitErp.Application.Common;
using KnitErp.Application.Organizations;
using KnitErp.Domain.Access;
using KnitErp.Domain.Audit;
using KnitErp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace KnitErp.IntegrationTests;

/// <summary>Аварийный доступ (D06): резервные коды входа и команда сервера emergency-access.</summary>
public sealed class EmergencyAccessTests(SqlTestHost host) : IClassFixture<SqlTestHost>
{
    private const string Password = "длинная парольная фраза для теста";
    private const string NewPassword = "другая длинная фраза после аварии";
    private static int _innSeed = 770_000_000;

    [SqlFact]
    public async Task Recovery_code_replaces_authenticator_once()
    {
        var (org, email, _) = await OwnerWithTwoFactorAsync();
        IReadOnlyList<string> codes;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            Assert.Equal(0, (await s.Recovery.GetStatusAsync()).Remaining);
            Assert.False(Item(await s.Readiness.GetAsync(), "emergency").Done);
            codes = await s.Recovery.IssueAsync();
            Assert.Equal(RecoveryCode.SetSize, codes.Distinct().Count());
            Assert.All(codes, c => Assert.Matches("^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$", c));
            Assert.Equal(RecoveryCode.SetSize, (await s.Recovery.GetStatusAsync()).Remaining);
        }

        // Код вводится без дефисов и строчными — тоже подходит; второй раз — нет.
        var typed = codes[0].Replace("-", "").ToLowerInvariant();
        var result = await SecondFactorAsync(email, typed);
        Assert.Equal(SignInStatus.Succeeded, result.Status);
        Assert.Equal(RecoveryCode.SetSize - 1, result.RecoveryCodesLeft);
        Assert.Equal(SignInStatus.Failed, (await SecondFactorAsync(email, codes[0])).Status);

        // Новый набор отменяет прежний.
        IReadOnlyList<string> fresh;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            fresh = await s.Recovery.IssueAsync();
        }

        Assert.Equal(SignInStatus.Failed, (await SecondFactorAsync(email, codes[1])).Status);
        Assert.Equal(SignInStatus.Succeeded, (await SecondFactorAsync(email, fresh[5])).Status);

        await using (var db = host.NewDb())
        {
            var actions = await db.AuditEntries.Where(a => a.OrganizationId == org.OrganizationId && a.EntityId == org.OwnerUserId.ToString())
                .Select(a => a.Action).ToListAsync();
            Assert.Equal(2, actions.Count(a => a == AuditActions.RecoveryCodeUsed));
            Assert.Equal(2, actions.Count(a => a == AuditActions.RecoveryCodesIssued));
            Assert.False(await db.RecoveryCodes.AnyAsync(r => r.CodeHash == System.Text.Encoding.ASCII.GetBytes(RecoveryCode.Normalize(fresh[0]))));
        }

        // Одного Владельца мало: пункт готовности просит второго администратора.
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var item = Item(await s.Readiness.GetAsync(), "emergency");
            Assert.False(item.Done);
            Assert.Contains("второй", item.Detail);
            Assert.DoesNotContain("нет резервных кодов", item.Detail);
        }
    }

    [SqlFact]
    public async Task Codes_need_two_factor_and_readiness_names_admins_without_codes()
    {
        var (org, _, _) = await OwnerWithTwoFactorAsync();
        long adminId;
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Recovery.IssueAsync();
            var invitation = await s.Access.InviteAsync(new InviteUserCommand($"admin-{Guid.NewGuid():N}@test.local", "Администратор",
                SystemRoles.Administrator, "Второй администратор"));
            adminId = invitation.UserId;
            await s.SignIn.AcceptInvitationAsync(invitation.SetupToken, Password, Password);
        }

        await using (var s = host.As(adminId, org.OrganizationId))
        {
            Assert.False((await s.Recovery.GetStatusAsync()).TwoFactorEnabled);
            Assert.Equal("auth.recovery.no_2fa", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Recovery.IssueAsync())).Code);
        }

        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            var item = Item(await s.Readiness.GetAsync(), "emergency");
            Assert.Contains("Администратор", item.Detail);
            Assert.DoesNotContain("второй", item.Detail);
        }

        await using (var db = host.NewDb())
        {
            Assert.Equal(RecoveryCode.SetSize, await db.RecoveryCodes.CountAsync(r => r.UserId == org.OwnerUserId));
        }
    }

    [SqlFact]
    public async Task Emergency_restore_resets_two_factor_and_issues_password_link()
    {
        var (org, email, _) = await OwnerWithTwoFactorAsync();
        await using (var s = host.As(org.OwnerUserId, org.OrganizationId))
        {
            await s.Recovery.IssueAsync();
        }

        // Пять ошибок — вход закрыт.
        for (var i = 0; i < SignInPolicy.MaxFailedAttempts; i++)
        {
            await using var s = host.As(null, null);
            await s.SignIn.PasswordSignInAsync(email, "неверный пароль номер " + i);
        }

        EmergencyAccessResult result;
        await using (var s = host.As(null, null))
        {
            Assert.Equal("auth.emergency.reason", (await Assert.ThrowsAsync<BusinessRuleException>(() => s.Recovery.EmergencyRestoreAsync(email, ""))).Code);
            await Assert.ThrowsAsync<NotFoundException>(() => s.Recovery.EmergencyRestoreAsync("nobody@test.local", "потерян телефон"));
            result = await s.Recovery.EmergencyRestoreAsync(email.ToUpperInvariant(), "потерян телефон, акт 1");
        }

        await using (var db = host.NewDb())
        {
            var user = await db.Users.SingleAsync(u => u.Id == org.OwnerUserId);
            Assert.False(user.TwoFactorEnabled);
            Assert.False(user.IsLockedOut(host.Clock.UtcNow));
            Assert.False(await db.RecoveryCodes.AnyAsync(r => r.UserId == org.OwnerUserId));
            var audit = await db.AuditEntries.SingleAsync(a => a.OrganizationId == org.OrganizationId && a.Action == AuditActions.EmergencyAccess);
            Assert.Null(audit.ActorUserId);
            Assert.Contains("потерян телефон", audit.Reason);
        }

        await using (var s = host.As(null, null))
        {
            await s.SignIn.AcceptInvitationAsync(result.RecoveryToken, NewPassword, NewPassword);
        }

        await using (var s = host.As(null, null))
        {
            Assert.Equal(SignInStatus.Failed, (await s.SignIn.PasswordSignInAsync(email, Password)).Status);
        }

        await using (var s = host.As(null, null))
        {
            Assert.Equal(SignInStatus.TwoFactorSetupRequired, (await s.SignIn.PasswordSignInAsync(email, NewPassword)).Status);
        }
    }

    private static ReadinessItem Item(LaunchReadinessDto dto, string id) => dto.Items.Single(i => i.Id == id);

    private async Task<SignInResult> SecondFactorAsync(string email, string code)
    {
        host.Clock.UtcNow = host.Clock.UtcNow.AddSeconds(Totp.StepSeconds);
        PendingSignIn pending;
        await using (var s = host.As(null, null))
        {
            var first = await s.SignIn.PasswordSignInAsync(email, Password);
            Assert.Equal(SignInStatus.TwoFactorRequired, first.Status);
            pending = first.Pending!;
        }

        await using (var s = host.As(null, null))
        {
            return await s.SignIn.TwoFactorSignInAsync(pending, code);
        }
    }

    private async Task<(CreatedOrganization Org, string Email, string Key)> OwnerWithTwoFactorAsync()
    {
        var inn = NextValidInn();
        var email = $"owner-{inn}@test.local";
        CreatedOrganization org;
        await using (var s = host.As(null, null))
        {
            org = await s.Organizations.CreateWithOwnerAsync(new CreateOrganizationCommand(
                $"Тестовая организация {inn}", $"Тест {inn}", inn, null, false, "Europe/Moscow", email, $"Владелец {inn}"));
            await s.SignIn.AcceptInvitationAsync(org.OwnerSetupToken, Password, Password);
        }

        PendingSignIn pending;
        await using (var s = host.As(null, null))
        {
            pending = (await s.SignIn.PasswordSignInAsync(email, Password)).Pending!;
        }

        string key;
        await using (var s = host.As(null, null))
        {
            key = (await s.SignIn.BeginTwoFactorSetupAsync(pending)).Key;
        }

        await using (var s = host.As(null, null))
        {
            Assert.Equal(SignInStatus.Succeeded,
                (await s.SignIn.CompleteTwoFactorSetupAsync(pending, Totp.Compute(key, Totp.StepAt(host.Clock.UtcNow)))).Status);
        }

        return (org, email, key);
    }

    private static string NextValidInn()
    {
        int[] w = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var body = Interlocked.Increment(ref _innSeed).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (body[i] - '0') * w[i];
        }

        return body + (sum % 11 % 10);
    }
}
