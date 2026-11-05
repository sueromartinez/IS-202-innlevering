using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// Snakker med en lokal språkmodell via Ollama (https://ollama.com).
/// Gratis, ingen API-nøkkel, og ingen data forlater maskinen.
///
/// Bruker Ollamas /api/chat med verktøy (tool calling).
/// </summary>
public class OllamaChatClient : IChatModelClient
{
    private readonly HttpClient _httpClient;

    private readonly SamiOptions.OllamaSettings _settings;


    public OllamaChatClient(
        HttpClient httpClient,
        IOptions<SamiOptions> options
    )
    {
        _settings = options.Value.Ollama;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }


    public async Task<ChatModelResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken
    )
    {
        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["stream"] = false,
            ["messages"] = BuildMessages(systemPrompt, messages),
            ["tools"] = BuildTools(tools),
            // Lav temperatur gir mer forutsigbare svar og verktøykall.
            ["options"] = new JsonObject { ["temperature"] = 0.2 }
        };

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync("api/chat", body, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ChatModelUnavailableException(
                "Fikk ikke kontakt med den lokale språkmodellen (Ollama).", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new ChatModelUnavailableException(
                $"Ollama svarte med {(int)response.StatusCode}: {error}");
        }

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

        JsonElement message = document.RootElement.GetProperty("message");

        string? text =
            message.TryGetProperty("content", out JsonElement content)
                ? content.GetString()
                : null;

        var toolCalls = new List<ChatToolCall>();

        if (message.TryGetProperty("tool_calls", out JsonElement calls)
            && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement call in calls.EnumerateArray())
            {
                JsonElement function = call.GetProperty("function");

                toolCalls.Add(new ChatToolCall
                {
                    // Ollama gir ikke alltid en ID, så vi lager en selv.
                    Id = "call_" + Guid.NewGuid().ToString("N")[..12],
                    Name = function.GetProperty("name").GetString() ?? string.Empty,
                    Arguments = ReadArguments(function)
                });
            }
        }

        return new ChatModelResponse
        {
            Text = text,
            ToolCalls = toolCalls
        };
    }


    private static JsonArray BuildMessages(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages
    )
    {
        var result = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt }
        };

        foreach (ChatMessage message in messages)
        {
            switch (message.Role)
            {
                case ChatRole.User:
                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = message.Text ?? string.Empty
                    });
                    break;

                case ChatRole.Assistant:
                    var assistant = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = message.Text ?? string.Empty
                    };

                    if (message.ToolCalls.Count > 0)
                    {
                        var calls = new JsonArray();

                        foreach (ChatToolCall call in message.ToolCalls)
                        {
                            calls.Add(new JsonObject
                            {
                                ["function"] = new JsonObject
                                {
                                    ["name"] = call.Name,
                                    ["arguments"] = JsonNode.Parse(call.Arguments.GetRawText())
                                }
                            });
                        }

                        assistant["tool_calls"] = calls;
                    }

                    result.Add(assistant);
                    break;

                case ChatRole.Tool:
                    result.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_name"] = message.ToolName,
                        ["content"] = message.Text ?? string.Empty
                    });
                    break;
            }
        }

        return result;
    }


    private static JsonArray BuildTools(IReadOnlyList<ChatToolDefinition> tools)
    {
        var result = new JsonArray();

        foreach (ChatToolDefinition tool in tools)
        {
            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    // DeepClone fordi en JsonNode bare kan ha én forelder.
                    ["parameters"] = tool.Parameters.DeepClone()
                }
            });
        }

        return result;
    }


    /// <summary>
    /// Ollama sender vanligvis argumentene som et objekt,
    /// men enkelte modeller sender dem som en JSON-tekst.
    /// </summary>
    private static JsonElement ReadArguments(JsonElement function)
    {
        if (!function.TryGetProperty("arguments", out JsonElement arguments))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        if (arguments.ValueKind == JsonValueKind.String)
        {
            try
            {
                return JsonDocument.Parse(arguments.GetString() ?? "{}").RootElement.Clone();
            }
            catch (JsonException)
            {
                return JsonDocument.Parse("{}").RootElement.Clone();
            }
        }

        return arguments.Clone();
    }
}
