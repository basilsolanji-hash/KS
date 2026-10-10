using KnitErp.Application.Common;
using KnitErp.Application.Organizations;

namespace KnitErp.UnitTests;

public sealed class PilotPreparationTests
{
    [Fact]
    public void Provisioning_parses_both_option_styles()
    {
        var (request, errors) = OrganizationProvisioning.Parse(
        [
            "--name", "Общество с ограниченной ответственностью «Солвер»", "--short-name=ООО «Солвер»",
            "--inn", "9705239429", "--kpp", "770501001", "--kpp-verified=yes",
            "--owner-email", "owner@factory.example", "--owner-name", "Владелец", "--url", "https://erp.factory.example/",
        ]);

        Assert.Empty(errors);
        Assert.NotNull(request);
        Assert.Equal("ООО «Солвер»", request.Command.ShortName);
        Assert.True(request.Command.KppVerified);
        Assert.Equal("Europe/Moscow", request.Command.TimeZoneId);
        Assert.Equal("https://erp.factory.example/account/invite?token=a%2Bb", OrganizationProvisioning.SetupLink(request.BaseUrl, "a+b"));
    }

    [Fact]
    public void Provisioning_lists_every_problem()
    {
        var (request, errors) = OrganizationProvisioning.Parse(["--inn", "9705239429", "--kpp-verified=может", "--color", "red", "--url", "http://x"]);

        Assert.Null(request);
        Assert.Contains(errors, e => e.Contains("--name"));
        Assert.Contains(errors, e => e.Contains("--owner-email"));
        Assert.Contains(errors, e => e.Contains("--kpp-verified"));
        Assert.Contains(errors, e => e.Contains("--color"));
        Assert.Contains(errors, e => e.Contains("https://"));
    }

    [Fact]
    public void Provisioning_refuses_verified_flag_without_kpp()
    {
        var (_, errors) = OrganizationProvisioning.Parse(
            ["--name", "ООО", "--short-name", "ООО", "--inn", "9705239429", "--kpp-verified", "yes", "--owner-email", "a@b.c", "--owner-name", "А"]);
        Assert.Contains(errors, e => e.Contains("нечего сверять"));
    }

    [Theory]
    [InlineData("09.10.2026", 2026, 10, 9)]
    [InlineData("9.10.2026", 2026, 10, 9)]
    [InlineData("2026-10-09", 2026, 10, 9)]
    [InlineData("10/9/2026", 2026, 10, 9)]
    [InlineData("09.10.2026 0:00:00", 2026, 10, 9)]
    [InlineData("46304", 2026, 10, 9)]
    public void Import_dates_accept_common_cell_formats(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), TableImport.ParseDate(text));

    [Theory]
    [InlineData("")]
    [InlineData("вчера")]
    [InlineData("31.02.2026")]
    public void Import_dates_reject_garbage(string text) => Assert.Null(TableImport.ParseDate(text));

    [Theory]
    [InlineData("да", true)]
    [InlineData("Да", true)]
    [InlineData("+", true)]
    [InlineData("", false)]
    [InlineData("нет", false)]
    [InlineData("может", null)]
    public void Import_yes_no(string text, bool? expected) => Assert.Equal(expected, TableImport.ParseYesNo(text));
}

public sealed class RecoveryCodeTests
{
    [Fact]
    public void Issued_codes_are_unique_readable_and_stored_as_hashes()
    {
        var (codes, records) = KnitErp.Domain.Access.RecoveryCode.Issue(7, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(KnitErp.Domain.Access.RecoveryCode.SetSize, codes.Distinct().Count());
        Assert.All(codes, c => Assert.DoesNotContain(c, ch => "01ILO".Contains(ch)));
        Assert.All(codes, c => Assert.True(KnitErp.Domain.Access.RecoveryCode.LooksLikeCode(c)));
        Assert.Equal(records[0].CodeHash, KnitErp.Domain.Access.RecoveryCode.Hash(codes[0].ToLowerInvariant().Replace("-", " ")));
        Assert.All(records, r => Assert.Equal(32, r.CodeHash.Length));
    }

    [Theory]
    [InlineData("123456", false)]
    [InlineData("123 456", false)]
    [InlineData("ABCD-EFGH-JKMN", true)]
    [InlineData("abcdefghjkmn", true)]
    public void Recovery_code_is_told_apart_from_authenticator_code(string input, bool expected) =>
        Assert.Equal(expected, KnitErp.Domain.Access.RecoveryCode.LooksLikeCode(input));

    [Fact]
    public void Emergency_command_requires_email_and_reason()
    {
        var (request, errors) = EmergencyCommand.Parse(["--email", "owner@factory.example", "--reason=потерян телефон"]);
        Assert.Empty(errors);
        Assert.Equal("потерян телефон", request!.Reason);

        var (bad, problems) = EmergencyCommand.Parse(["--email", "x@y.z", "--force"]);
        Assert.Null(bad);
        Assert.Contains(problems, p => p.Contains("--force"));
        Assert.Contains(problems, p => p.Contains("--reason"));
    }
}
