using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using KnitErp.Application.Workspace;

namespace KnitErp.Infrastructure.External;

/// <summary>
/// ИИ-помощник на Claude через официальный SDK Anthropic. Включается только ключом в настройках сервера
/// (Assistant:ApiKey или переменная ANTHROPIC_API_KEY); ключ в репозитории не хранится.
/// В модель уходят вопрос, раздел системы, роли и права пользователя и текст справки — без учётных данных фабрики.
/// </summary>
public sealed class ClaudeAssistantModel : IAssistantModel
{
    public const string DefaultModel = "claude-opus-5-5";

    private readonly AnthropicClient? _client;
    private readonly string _model;

    public ClaudeAssistantModel(string? apiKey, string? model)
    {
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // Короткий таймаут и одна повторная попытка: помощник не должен держать интерфейс.
            _client = new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(45), MaxRetries = 1 };
        }
    }

    public bool IsConfigured => _client is not null;

    public async Task<string> AskAsync(string systemPrompt, IReadOnlyList<AssistantTurn> conversation, CancellationToken ct = default)
    {
        if (_client is null)
        {
            throw new ExternalServiceUnavailableException("ИИ-помощник не подключён.");
        }

        try
        {
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 2048,
                System = systemPrompt,
                // Справочные вопросы — короткие ответы: низкое усилие экономит время и токены.
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
                // Если модель откажет по правилам безопасности, ответ даст резервная модель в том же вызове.
                Betas = ["server-side-fallback-2026-06-01"],
                Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } },
                Messages = conversation.Select(t => new BetaMessageParam
                {
                    Role = t.FromUser ? Role.User : Role.Assistant,
                    Content = t.Text,
                }).ToList(),
            }, ct);

            if (response.StopReason == "refusal")
            {
                return "Не могу ответить на этот вопрос. Спросите о работе в knitERP или откройте справочный центр.";
            }

            var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text)).Trim();
            return text.Length > 0 ? text : "Ответ пустой — переформулируйте вопрос.";
        }
        catch (AnthropicRateLimitException)
        {
            throw new ExternalServiceUnavailableException("ИИ-помощник перегружен — попробуйте через минуту.");
        }
        catch (Anthropic5xxException)
        {
            throw new ExternalServiceUnavailableException("ИИ-помощник временно недоступен.");
        }
        catch (AnthropicIOException)
        {
            throw new ExternalServiceUnavailableException("Нет связи с ИИ-помощником.");
        }
        catch (AnthropicApiException)
        {
            throw new ExternalServiceUnavailableException("ИИ-помощник не смог ответить — проверьте настройку ключа.");
        }
    }
}
