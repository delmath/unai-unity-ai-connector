using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnAI.Config;
using UnAI.Core;
using UnAI.Models;

namespace UnAI.Providers.OpenCodeGo
{
    /// <summary>
    /// OpenCode Go is a low-cost subscription gateway that exposes open coding models
    /// through a single API key. Depending on the model, the gateway speaks one of
    /// three different protocols:
    /// <list type="bullet">
    ///   <item><c>/v1/chat/completions</c> — OpenAI-compatible (GLM, Kimi, DeepSeek, MiMo, Hy, LongCat)</item>
    ///   <item><c>/v1/messages</c> — Anthropic Messages (MiniMax, Qwen)</item>
    ///   <item><c>/v1/responses</c> — OpenAI Responses (Grok, GPT 5.6 Luna, Muse Spark)</item>
    /// </list>
    /// This provider routes each request to the correct sub-provider based on the
    /// requested model, so callers only ever see the single <c>opencode-go</c> provider.
    /// </summary>
    public class OpenCodeGoProvider : IUnaiProvider
    {
        public const string Id = "opencode-go";

        private UnaiProviderConfig _config;
        private IUnaiProvider _chat;
        private IUnaiProvider _messages;
        private IUnaiProvider _responses;

        public string ProviderId => Id;
        public string DisplayName => "OpenCode Go";
        public bool SupportsToolCalling => true;

        public bool IsConfigured =>
            _config != null &&
            !string.IsNullOrEmpty(_config.BaseUrl) &&
            !string.IsNullOrEmpty(_config.ResolvedApiKey);

        public IReadOnlyList<UnaiModelInfo> KnownModels => AllModels;

        public void Initialize(UnaiProviderConfig config)
        {
            _config = config;
            if (config == null) return;

            _chat = new OpenCodeGoChatProvider();
            _chat.Initialize(BuildSubConfig(config, "opencode-go-chat"));

            _messages = new OpenCodeGoMessagesProvider();
            _messages.Initialize(BuildSubConfig(config, "opencode-go-messages"));

            _responses = new OpenCodeGoResponsesProvider();
            _responses.Initialize(BuildSubConfig(config, "opencode-go-responses"));
        }

        public async Task<UnaiChatResponse> ChatAsync(
            UnaiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            var provider = Resolve(request?.Model);

            try
            {
                var response = await provider.ChatAsync(request, cancellationToken);
                response.ProviderId = ProviderId;
                return response;
            }
            catch (UnaiRequestException ex)
            {
                ex.ErrorInfo.ProviderId = ProviderId;
                throw;
            }
        }

        public Task ChatStreamAsync(
            UnaiChatRequest request,
            Action<UnaiStreamDelta> onDelta,
            Action<UnaiChatResponse> onComplete = null,
            Action<UnaiErrorInfo> onError = null,
            CancellationToken cancellationToken = default)
        {
            var provider = Resolve(request?.Model);

            return provider.ChatStreamAsync(
                request,
                onDelta,
                onComplete: response =>
                {
                    response.ProviderId = ProviderId;
                    onComplete?.Invoke(response);
                },
                onError: error =>
                {
                    error.ProviderId = ProviderId;
                    onError?.Invoke(error);
                },
                cancellationToken);
        }

        private IUnaiProvider Resolve(string model)
        {
            if (_chat == null)
                throw new InvalidOperationException(
                    "[UNAI] OpenCode Go provider has not been initialized. Assign a valid provider config.");

            if (!string.IsNullOrEmpty(model))
            {
                if (MessagesModels.Contains(model)) return _messages;
                if (ResponsesModels.Contains(model)) return _responses;
            }

            return _chat;
        }

        private static UnaiProviderConfig BuildSubConfig(UnaiProviderConfig source, string subId)
        {
            return new UnaiProviderConfig
            {
                ProviderId = subId,
                Enabled = true,
                BaseUrl = source.BaseUrl,
                ApiKey = source.ApiKey,
                ApiKeyEnvironmentVariable = source.ApiKeyEnvironmentVariable,
                DefaultModel = source.DefaultModel,
                TimeoutSeconds = source.TimeoutSeconds,
                MaxRetries = source.MaxRetries,
                CustomHeaders = source.CustomHeaders
            };
        }

        // ── Model catalog ───────────────────────────────────────────────
        private static readonly HashSet<string> MessagesModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "minimax-m3", "minimax-m2.7", "minimax-m2.5",
            "qwen3.8-max", "qwen3.8-flash", "qwen3.7-max", "qwen3.7-plus", "qwen3.6-plus"
        };

        private static readonly HashSet<string> ResponsesModels = new(StringComparer.OrdinalIgnoreCase)
        {
            "grok-4.6", "gpt-5.6-luna",
            "muse-spark-1.3-contributor", "muse-spark-1.2-contributor"
        };

        private static readonly UnaiModelInfo[] AllModels =
        {
            // chat/completions
            new() { Id = "glm-5.3-flash", DisplayName = "GLM-5.3 Flash", ProviderId = Id },
            new() { Id = "glm-5.3", DisplayName = "GLM-5.3", ProviderId = Id },
            new() { Id = "glm-5.2", DisplayName = "GLM-5.2", ProviderId = Id },
            new() { Id = "glm-5.1", DisplayName = "GLM-5.1", ProviderId = Id },
            new() { Id = "kimi-k3", DisplayName = "Kimi K3", ProviderId = Id },
            new() { Id = "kimi-k2.7-code", DisplayName = "Kimi K2.7 Code", ProviderId = Id },
            new() { Id = "kimi-k2.6", DisplayName = "Kimi K2.6", ProviderId = Id },
            new() { Id = "longcat-2.0", DisplayName = "LongCat 2.0", ProviderId = Id },
            new() { Id = "deepseek-v4.1-flash", DisplayName = "DeepSeek V4.1 Flash", ProviderId = Id },
            new() { Id = "deepseek-v4-pro", DisplayName = "DeepSeek V4 Pro", ProviderId = Id },
            new() { Id = "deepseek-v4-flash", DisplayName = "DeepSeek V4 Flash", ProviderId = Id },
            new() { Id = "deepseek-v4-flash-vision-exp", DisplayName = "DeepSeek V4 Flash Vision (Exp)", ProviderId = Id },
            new() { Id = "mimo-v2.5", DisplayName = "MiMo V2.5", ProviderId = Id },
            new() { Id = "mimo-v2.5-pro", DisplayName = "MiMo V2.5 Pro", ProviderId = Id },
            new() { Id = "hy4-preview", DisplayName = "Hy4 Preview", ProviderId = Id },
            new() { Id = "hy3", DisplayName = "Hy3", ProviderId = Id },

            // messages
            new() { Id = "minimax-m3", DisplayName = "MiniMax M3", ProviderId = Id },
            new() { Id = "minimax-m2.7", DisplayName = "MiniMax M2.7", ProviderId = Id },
            new() { Id = "minimax-m2.5", DisplayName = "MiniMax M2.5", ProviderId = Id },
            new() { Id = "qwen3.8-max", DisplayName = "Qwen3.8 Max", ProviderId = Id },
            new() { Id = "qwen3.8-flash", DisplayName = "Qwen3.8 Flash", ProviderId = Id },
            new() { Id = "qwen3.7-max", DisplayName = "Qwen3.7 Max", ProviderId = Id },
            new() { Id = "qwen3.7-plus", DisplayName = "Qwen3.7 Plus", ProviderId = Id },
            new() { Id = "qwen3.6-plus", DisplayName = "Qwen3.6 Plus", ProviderId = Id },

            // responses
            new() { Id = "grok-4.6", DisplayName = "Grok 4.6", ProviderId = Id },
            new() { Id = "gpt-5.6-luna", DisplayName = "GPT 5.6 Luna", ProviderId = Id },
            new() { Id = "muse-spark-1.3-contributor", DisplayName = "Muse Spark 1.3 Contributor", ProviderId = Id },
            new() { Id = "muse-spark-1.2-contributor", DisplayName = "Muse Spark 1.2 Contributor", ProviderId = Id },
        };
    }
}
