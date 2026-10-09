using KnitErp.Application.Authentication;
using KnitErp.Domain.Access;
using QRCoder;

namespace KnitErp.Web.Security;

public static class SignInMessages
{
    /// <summary>Текст не раскрывает, существует ли email: неверный email и неверный пароль выглядят одинаково.</summary>
    public static string For(SignInStatus status) => status switch
    {
        SignInStatus.LockedOut =>
            $"Слишком много неудачных попыток. Вход закрыт на {SignInPolicy.LockoutDuration.TotalMinutes:0} минут, попробуйте позже.",
        SignInStatus.NoOrganization => "Нет доступа ни к одной организации. Обратитесь к администратору.",
        _ => "Неверный email или пароль.",
    };
}

public static class QrCodes
{
    public static string PngDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        return "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(5));
    }
}
