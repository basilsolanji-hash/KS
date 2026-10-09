using KnitErp.Web.Localization;
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
            Text.L("Слишком много неудачных попыток. Вход закрыт на {0} минут, попробуйте позже.", SignInPolicy.LockoutDuration.TotalMinutes),
        SignInStatus.NoOrganization => Text.L("Нет доступа ни к одной организации. Обратитесь к администратору."),
        _ => Text.L("Неверный email или пароль."),
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
