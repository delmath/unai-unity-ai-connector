using System;
using System.Collections.Generic;
using UnAI.Providers;
using UnAI.Providers.Anthropic;
using UnAI.Providers.OpenAICompatible;

namespace UnAI.Providers.OpenCodeGo
{
    /// <summary>
    /// Shared headers required by the OpenCode Go gateway. The gateway asks clients
    /// to identify themselves with a custom user agent and to send a stable session
    /// id in <c>x-opencode-session</c> so it can optimise routing and prompt caching.
    /// </summary>
    internal static class OpenCodeGoHeaders
    {
        private const string UserAgent = "unai-unity-ai-connector/" + UnaiVersion.Current;
        private static readonly string SessionId = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Adds the OpenCode Go telemetry headers. Existing values (e.g. ones set via
        /// the provider's CustomHeaders) are preserved.
        /// </summary>
        public static void Apply(Dictionary<string, string> headers)
        {
            if (headers == null) return;

            if (!headers.ContainsKey("User-Agent"))
                headers["User-Agent"] = UserAgent;

            if (!headers.ContainsKey("x-opencode-session"))
                headers["x-opencode-session"] = SessionId;
        }
    }

    /// <summary>OpenAI-compatible chat completions sub-provider for OpenCode Go.</summary>
    internal class OpenCodeGoChatProvider : OpenAICompatibleProvider
    {
        public override string ProviderId => "opencode-go-chat";
        public override string DisplayName => "OpenCode Go (Chat)";

        protected override Dictionary<string, string> BuildHeaders()
        {
            var headers = base.BuildHeaders();
            OpenCodeGoHeaders.Apply(headers);
            return headers;
        }
    }

    /// <summary>Anthropic Messages sub-provider for OpenCode Go.</summary>
    internal class OpenCodeGoMessagesProvider : AnthropicProvider
    {
        public override string ProviderId => "opencode-go-messages";
        public override string DisplayName => "OpenCode Go (Messages)";

        protected override Dictionary<string, string> BuildHeaders()
        {
            var headers = base.BuildHeaders();

            string apiKey = Config.ResolvedApiKey;
            if (!string.IsNullOrEmpty(apiKey))
                headers["Authorization"] = $"Bearer {apiKey}";

            ApplyCustomHeaders(headers);
            OpenCodeGoHeaders.Apply(headers);
            return headers;
        }
    }

    /// <summary>OpenAI Responses API sub-provider for OpenCode Go.</summary>
    internal class OpenCodeGoResponsesProvider : OpenAIResponsesBase
    {
        public override string ProviderId => "opencode-go-responses";
        public override string DisplayName => "OpenCode Go (Responses)";

        protected override Dictionary<string, string> BuildHeaders()
        {
            var headers = base.BuildHeaders();
            OpenCodeGoHeaders.Apply(headers);
            return headers;
        }
    }
}
