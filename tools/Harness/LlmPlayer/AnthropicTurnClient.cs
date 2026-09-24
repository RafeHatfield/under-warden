using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace UnderWarden.Harness.LlmPlayer;

/// <summary>
/// Thin wrapper around the Anthropic SDK for single-turn LLM calls.
/// Only file in Harness that knows about the Anthropic SDK.
///
/// The system prompt is sent with cache_control:ephemeral on each call so the
/// ~2650-token static block is cached server-side. Per-turn state descriptions
/// are sent as user messages (non-cacheable).
/// </summary>
public sealed class AnthropicTurnClient : IDisposable
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _systemPrompt;
    private readonly int _timeoutMs;

    public AnthropicTurnClient(string apiKey, string model, string systemPrompt, int timeoutMs = 10_000)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set");

        _client = new AnthropicClient(apiKey);
        _model = model;
        _systemPrompt = systemPrompt;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Call the model with the given user message. Returns the raw text response,
    /// or null + an error string on timeout, API error, or any exception.
    /// Never throws.
    /// </summary>
    public (string? Text, string? Error) CallSync(string userMessage, int maxTokens = 400)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timeoutMs);

            var systemMessages = new List<SystemMessage>
            {
                // System prompt with cache_control:ephemeral — the ~2650-token static block
                // is cached server-side after the first call.
                new SystemMessage(_systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
            };

            var messages = new List<Message>
            {
                new Message(RoleType.User, userMessage)
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                MaxTokens = maxTokens,
                Model = _model,
                Stream = false,
                System = systemMessages,
                // FineGrained: we manually set cache_control on the system message above.
                // This lets us cache only the static system prompt, not per-turn user messages.
                PromptCaching = PromptCacheType.FineGrained,
            };

            var response = _client.Messages
                .GetClaudeMessageAsync(parameters, cts.Token)
                .GetAwaiter()
                .GetResult();

            string? text = response.Message.ToString();

            // Phase 5 observability: log token usage to stderr so harness output can be
            // analyzed for cost and cache efficiency without parsing the transcript.
            // CacheReadInputTokens > 0 confirms the system prompt is being served from cache.
            if (response.Usage != null)
            {
                Console.Error.WriteLine(
                    $"[tokens] input={response.Usage.InputTokens} " +
                    $"output={response.Usage.OutputTokens} " +
                    $"cached={response.Usage.CacheReadInputTokens} " +
                    $"cache_created={response.Usage.CacheCreationInputTokens}");
            }

            return (text, null);
        }
        catch (OperationCanceledException)
        {
            return (null, $"timeout after {_timeoutMs}ms");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public void Dispose() => _client.Dispose();
}
