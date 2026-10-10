namespace KnitErp.Web.Security;

public static class ReturnUrls
{
    /// <summary>Только локальный путь: внешний адрес после входа — классическая уязвимость открытого редиректа.</summary>
    public static string Safe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var url = returnUrl.StartsWith('/') ? returnUrl : "/" + returnUrl;
        return url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/\\", StringComparison.Ordinal)
               || url.Contains("://", StringComparison.Ordinal) || url.StartsWith("/account/", StringComparison.OrdinalIgnoreCase)
            ? "/"
            : url;
    }
}
