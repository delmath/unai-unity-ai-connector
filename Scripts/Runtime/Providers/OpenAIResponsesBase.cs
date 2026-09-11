using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnAI.Core;
using UnAI.Models;
using UnAI.Streaming;
using UnAI.Tools;
using Newtonsoft.Json.Linq;

namespace UnAI.Providers
{
    /// <summary>
    /// Base class for providers that expose the OpenAI <b>Responses API</b>
    /// (<c>/v1/responses</c>) rather than the legacy chat completions API.
    ///
    /// The Responses API differs from <see cref="OpenAICompatibleBase"/> in a few
    /// important ways:
    /// <list type="bullet">
    ///   <item>the conversation is sent as a flat <c>input</c> array of items;</item>
    ///   <item>the system prompt is sent separately via <c>instructions</c>;</item>
    ///   <item>tool definitions are flat (<c>type: "function"</c>, <c>name</c>, <c>parameters</c>);</item>
    ///   <item>the response is an <c>output</c> array of message / function_call items;</item>
    ///   <item>streaming emits named events such as <c>response.output_text.delta</c>.</item>
    /// </list>
    /// </summary>
    public abstract class OpenAIResponsesBase : UnaiProviderBase
    {
        protected virtual string ResponsesEndpointPath => "/v1/responses";

        public override bool SupportsToolCalling => true;

        protected override string BuildRequestUrl(UnaiChatRequest request)
        {
            return Config.BaseUrl.TrimEnd('/') + ResponsesEndpointPath;
        }

        protected override Dictionary<string, string> BuildHeaders()
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            };

            string apiKey = Config.ResolvedApiKey;
            if (!string.IsNullOrEmpty(apiKey))
                headers["Authorization"] = $"Bearer {apiKey}";

            ApplyCustomHeaders(headers);

            return headers;
        }

        protected override string SerializeRequest(UnaiChatRequest request)
        {
            var obj = new JObject
            {
                ["model"] = request.Model,
                ["stream"] = request.Stream
            };

            // System messages become the top-level "instructions" string.
            var systemParts = request.Messages
                .Where(m => m.Role == UnaiRole.System)
                .Select(m => m.Content)
                .Where(c => !string.IsNullOrEmpty(c));
            string instructions = string.Join("\n\n", systemParts);
            if (!string.IsNullOrEmpty(instructions))
                obj["instructions"] = instructions;

            if (request.Tools is { Count: > 0 })
            {
                var toolsArray = new JArray();
                foreach (var tool in request.Tools)
                {
                    toolsArray.Add(new JObject
                    {
                        ["type"] = "function",
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = tool.ParametersSchema ?? new JObject { ["type"] = "object" }
                    });
                }
                obj["tools"] = toolsArray;
            }

            var input = new JArray();
            foreach (var msg in request.Messages.Where(m => m.Role != UnaiRole.System))
            {
                if (msg.Role == UnaiRole.Tool)
                {
                    input.Add(new JObject
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = msg.ToolCallId,
                        ["output"] = msg.Content ?? ""
                    });
                }
                else if (msg.Role == UnaiRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        input.Add(new JObject
                        {
                            ["role"] = "assistant",
                            ["content"] = msg.Content
                        });
                    }

                    foreach (var tc in msg.ToolCalls)
                    {
                        input.Add(new JObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = tc.Id,
                            ["name"] = tc.ToolName,
                            ["arguments"] = tc.ArgumentsJson ?? "{}"
                        });
                    }
                }
                else
                {
                    input.Add(new JObject
                    {
                        ["role"] = msg.Role == UnaiRole.User ? "user" : "assistant",
                        ["content"] = msg.Content ?? ""
                    });
                }
            }
            obj["input"] = input;

            if (request.Options != null)
            {
                if (request.Options.Temperature.HasValue)
                    obj["temperature"] = request.Options.Temperature.Value;
                if (request.Options.MaxTokens.HasValue)
                    obj["max_output_tokens"] = request.Options.MaxTokens.Value;
                if (request.Options.TopP.HasValue)
                    obj["top_p"] = request.Options.TopP.Value;

                if (request.Options.ResponseFormat == UnaiResponseFormat.JsonObject)
                {
                    obj["text"] = new JObject
                    {
                        ["format"] = new JObject { ["type"] = "json_object" }
                    };
                }
                else if (request.Options.ResponseFormat == UnaiResponseFormat.JsonSchema)
                {
                    var format = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = request.Options.JsonSchemaName ?? "response",
                        ["strict"] = true
                    };
                    if (request.Options.JsonSchema != null)
                        format["schema"] = request.Options.JsonSchema;

                    obj["text"] = new JObject { ["format"] = format };
                }
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        protected override UnaiChatResponse DeserializeResponse(string json)
        {
            var root = JObject.Parse(json);

            string content = "";
            List<UnaiToolCall> toolCalls = null;

            if (root["output"] is JArray output)
            {
                foreach (var item in output)
                {
                    string type = item["type"]?.ToString();

                    if (type == "message")
                    {
                        if (item["content"] is JArray parts)
                        {
                            foreach (var part in parts)
                            {
                                if (part["type"]?.ToString() == "output_text")
                                    content += part["text"]?.ToString() ?? "";
                            }
                        }
                    }
                    else if (type == "function_call")
                    {
                        toolCalls ??= new List<UnaiToolCall>();
                        toolCalls.Add(new UnaiToolCall
                        {
                            Id = item["call_id"]?.ToString() ?? item["id"]?.ToString(),
                            ToolName = item["name"]?.ToString(),
                            ArgumentsJson = item["arguments"]?.ToString() ?? "{}"
                        });
                    }
                }
            }

            int inputTokens = root["usage"]?["input_tokens"]?.Value<int>() ?? 0;
            int outputTokens = root["usage"]?["output_tokens"]?.Value<int>() ?? 0;
            int totalTokens = root["usage"]?["total_tokens"]?.Value<int>() ?? (inputTokens + outputTokens);

            return new UnaiChatResponse
            {
                Content = content,
                Role = UnaiRole.Assistant,
                Model = root["model"]?.ToString(),
                FinishReason = root["status"]?.ToString(),
                ToolCalls = toolCalls,
                Usage = new UnaiUsageInfo
                {
                    PromptTokens = inputTokens,
                    CompletionTokens = outputTokens,
                    TotalTokens = totalTokens
                }
            };
        }

        protected override ISseLineParser CreateStreamParser()
        {
            // Function calls are streamed as an item header followed by argument
            // fragments keyed by the output item id. Accumulate until completion.
            var toolCallAccum = new Dictionary<string, (string callId, string name, StringBuilder args)>();

            return new SseLineParser(
                deltaFactory: (eventType, jsonData) =>
                {
                    var root = JObject.Parse(jsonData);
                    string type = root["type"]?.ToString() ?? eventType;

                    switch (type)
                    {
                        case "response.output_text.delta":
                            return new UnaiStreamDelta
                            {
                                Content = root["delta"]?.ToString() ?? "",
                                EventType = type
                            };

                        case "response.output_item.added":
                        {
                            var item = root["item"];
                            if (item?["type"]?.ToString() == "function_call")
                            {
                                string itemId = item["id"]?.ToString();
                                if (!string.IsNullOrEmpty(itemId))
                                {
                                    toolCallAccum[itemId] = (
                                        item["call_id"]?.ToString(),
                                        item["name"]?.ToString(),
                                        new StringBuilder());
                                }
                            }
                            return null;
                        }

                        case "response.function_call_arguments.delta":
                        {
                            string itemId = root["item_id"]?.ToString();
                            string chunk = root["delta"]?.ToString();
                            if (!string.IsNullOrEmpty(itemId) && !string.IsNullOrEmpty(chunk)
                                && toolCallAccum.TryGetValue(itemId, out var entry))
                            {
                                entry.args.Append(chunk);
                            }
                            return null;
                        }

                        case "response.function_call_arguments.done":
                        {
                            string itemId = root["item_id"]?.ToString();
                            if (!string.IsNullOrEmpty(itemId) && toolCallAccum.TryGetValue(itemId, out var entry))
                            {
                                string args = root["arguments"]?.ToString();
                                if (!string.IsNullOrEmpty(args))
                                {
                                    entry.args.Clear();
                                    entry.args.Append(args);
                                }
                            }
                            return null;
                        }

                        case "response.output_item.done":
                        {
                            var item = root["item"];
                            if (item?["type"]?.ToString() == "function_call")
                            {
                                string itemId = item["id"]?.ToString();
                                if (!string.IsNullOrEmpty(itemId))
                                {
                                    string args = item["arguments"]?.ToString();
                                    if (toolCallAccum.TryGetValue(itemId, out var entry))
                                    {
                                        entry.callId = item["call_id"]?.ToString() ?? entry.callId;
                                        entry.name = item["name"]?.ToString() ?? entry.name;
                                        if (!string.IsNullOrEmpty(args))
                                        {
                                            entry.args.Clear();
                                            entry.args.Append(args);
                                        }
                                        toolCallAccum[itemId] = entry;
                                    }
                                    else
                                    {
                                        toolCallAccum[itemId] = (
                                            item["call_id"]?.ToString(),
                                            item["name"]?.ToString(),
                                            new StringBuilder(args ?? ""));
                                    }
                                }
                            }
                            return null;
                        }

                        case "response.completed":
                        {
                            var response = root["response"];
                            int inTok = response?["usage"]?["input_tokens"]?.Value<int>() ?? 0;
                            int outTok = response?["usage"]?["output_tokens"]?.Value<int>() ?? 0;
                            int totTok = response?["usage"]?["total_tokens"]?.Value<int>() ?? (inTok + outTok);

                            var delta = new UnaiStreamDelta
                            {
                                Content = "",
                                IsFinal = true,
                                FinishReason = response?["status"]?.ToString() ?? "completed",
                                EventType = type,
                                Usage = new UnaiUsageInfo
                                {
                                    PromptTokens = inTok,
                                    CompletionTokens = outTok,
                                    TotalTokens = totTok
                                }
                            };

                            if (toolCallAccum.Count > 0)
                            {
                                delta.ToolCalls = new List<UnaiToolCall>();
                                foreach (var kvp in toolCallAccum)
                                {
                                    delta.ToolCalls.Add(new UnaiToolCall
                                    {
                                        Id = kvp.Value.callId,
                                        ToolName = kvp.Value.name,
                                        ArgumentsJson = kvp.Value.args.ToString()
                                    });
                                }
                            }
                            return delta;
                        }

                        case "response.failed":
                        case "error":
                            return new UnaiStreamDelta
                            {
                                Content = "",
                                IsFinal = true,
                                EventType = type,
                                FinishReason = "error"
                            };

                        default:
                            return null;
                    }
                },
                doneMarker: "__OPENAI_RESPONSES_NO_DONE__");
        }
    }
}
