namespace KnitErp.Application.Organizations;

/// <summary>Параметры команды сервера «create-organization»: команда создания и адрес программы для ссылки Владельца.</summary>
public sealed record ProvisioningRequest(CreateOrganizationCommand Command, string? BaseUrl);

/// <summary>
/// Разбор параметров команды «dotnet KnitErp.Web.dll create-organization --inn … --kpp … --kpp-verified=yes --name «…»
/// --short-name «…» --owner-email … --owner-name «…» [--timezone Europe/Moscow] [--url https://erp.example]».
/// Реквизиты проверяет доменная сущность при создании; здесь — только наличие и формат параметров.
/// </summary>
public static class OrganizationProvisioning
{
    public const string CommandName = "create-organization";

    public static readonly IReadOnlyList<string> Usage =
    [
        $"dotnet KnitErp.Web.dll {CommandName} \\",
        "  --name \"Общество с ограниченной ответственностью «…»\" --short-name \"ООО «…»\" \\",
        "  --inn 1234567890 --kpp 123456789 --kpp-verified=yes \\",
        "  --owner-email owner@factory.example --owner-name \"Фамилия Имя\" \\",
        "  [--timezone Europe/Moscow] [--url https://erp.factory.example]",
    ];

    private static readonly HashSet<string> Known =
        ["name", "short-name", "inn", "kpp", "kpp-verified", "owner-email", "owner-name", "timezone", "url"];

    /// <summary>Аргументы после имени команды. Ошибки — понятным списком, без исключений.</summary>
    public static (ProvisioningRequest? Request, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Непонятный параметр «{arg}»: ожидается --имя значение.");
                continue;
            }

            var eq = arg.IndexOf('=');
            string key, value;
            if (eq > 0)
            {
                key = arg[2..eq];
                value = arg[(eq + 1)..];
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                key = arg[2..];
                value = args[++i];
            }
            else
            {
                key = arg[2..];
                value = "";
            }

            if (!Known.Contains(key))
            {
                errors.Add($"Неизвестный параметр --{key}.");
                continue;
            }

            values[key] = value.Trim();
        }

        string Required(string key, string label)
        {
            if (values.TryGetValue(key, out var v) && v.Length > 0)
            {
                return v;
            }

            errors.Add($"Не указан параметр --{key} ({label}).");
            return "";
        }

        var name = Required("name", "полное наименование");
        var shortName = Required("short-name", "краткое наименование");
        var inn = Required("inn", "ИНН");
        var ownerEmail = Required("owner-email", "почта Владельца");
        var ownerName = Required("owner-name", "имя Владельца");
        var kpp = values.GetValueOrDefault("kpp") is { Length: > 0 } k ? k : null;

        var verifiedText = values.GetValueOrDefault("kpp-verified") ?? "";
        bool? verified = verifiedText.ToLowerInvariant() switch
        {
            "yes" or "да" or "true" => true,
            "" or "no" or "нет" or "false" => false,
            _ => null,
        };
        if (verified is null)
        {
            errors.Add("--kpp-verified: укажите yes или no.");
        }

        if (kpp is null && verified == true)
        {
            errors.Add("--kpp-verified=yes без --kpp: нечего сверять.");
        }

        var url = values.GetValueOrDefault("url") is { Length: > 0 } u ? u.TrimEnd('/') : null;
        if (url is not null && !(Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps))
        {
            errors.Add("--url: адрес программы должен начинаться с https://.");
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var timeZone = values.GetValueOrDefault("timezone") is { Length: > 0 } tz ? tz : "Europe/Moscow";
        return (new ProvisioningRequest(
            new CreateOrganizationCommand(name, shortName, inn, kpp, verified == true, timeZone, ownerEmail, ownerName), url), errors);
    }

    /// <summary>Ссылка установки пароля Владельца: полная, если известен адрес программы.</summary>
    public static string SetupLink(string? baseUrl, string token) =>
        $"{baseUrl ?? ""}/account/invite?token={Uri.EscapeDataString(token)}";
}

/// <summary>Параметры команды сервера «emergency-access --email … --reason "…" [--url https://…]».</summary>
public sealed record EmergencyRequest(string Email, string Reason, string? BaseUrl);

public static class EmergencyCommand
{
    public const string Name = "emergency-access";

    public const string Usage =
        "Пример: dotnet KnitErp.Web.dll emergency-access --email owner@factory.example --reason \"потерян телефон, акт №1\" [--url https://erp.factory.example]";

    public static (EmergencyRequest? Request, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var eq = arg.IndexOf('=');
            var key = arg.StartsWith("--", StringComparison.Ordinal) ? (eq > 0 ? arg[2..eq] : arg[2..]) : "";
            if (key is not ("email" or "reason" or "url"))
            {
                errors.Add($"Непонятный параметр «{arg}».");
                continue;
            }

            values[key] = (eq > 0 ? arg[(eq + 1)..] : i + 1 < args.Count ? args[++i] : "").Trim();
        }

        var email = values.GetValueOrDefault("email") ?? "";
        var reason = values.GetValueOrDefault("reason") ?? "";
        if (email.Length == 0)
        {
            errors.Add("Не указан параметр --email (почта пользователя).");
        }

        if (reason.Length < 5)
        {
            errors.Add("Не указан параметр --reason (причина для журнала аудита, не короче 5 символов).");
        }

        var url = values.GetValueOrDefault("url") is { Length: > 0 } u ? u.TrimEnd('/') : null;
        if (url is not null && !(Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps))
        {
            errors.Add("--url: адрес программы должен начинаться с https://.");
        }

        return errors.Count > 0 ? (null, errors) : (new EmergencyRequest(email, reason, url), errors);
    }
}
