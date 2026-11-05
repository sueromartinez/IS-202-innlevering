using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// Snakker med Claude via Anthropics Messages API.
/// Aktiveres med "Sami:Provider": "Claude" og en API-nøkkel i
/// miljøvariabelen ANTHROPIC_API_KEY (eller Sami:Claude:ApiKey).
///
/// API-nøkkelen ligger bare på serveren og sendes aldri til nettleseren.
/// </summary>
public class ClaudeChatClient : IChatModelClient
{
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _httpClient;

    private readonly SamiOptions.ClaudeSettings _settings;

    private readonly string? _apiKey;


    public ClaudeChatClient(
        HttpClient httpClient,
        IOptions<SamiOptions> options,
        IConfiguration configuration
    )
    {
        _settings = options.Value.Claude;

        _apiKey =
            string.IsNullOrWhiteSpace(_settings.ApiKey)
                ? configuration["ANTHROPIC_API_KEY"]
                : _settings.ApiKey;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }


    public async Task<ChatModelResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ChatModelUnavailableException(
                "Claude er valgt, men ANTHROPIC_API_KEY er ikke satt.");
        }

        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["max_tokens"] = _settings.MaxTokens,
            ["system"] = systemPrompt,
            ["messages"] = BuildMessages(messages),
            ["tools"] = BuildTools(tools)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ChatModelUnavailableException("Fikk ikke kontakt med Claude API.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new ChatModelUnavailableException(
                $"Claude API svarte med {(int)response.StatusCode}: {error}");
        }

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

        var text = new StringBuilder();
        var toolCalls = new List<ChatToolCall>();

        foreach (JsonElement block in document.RootElement.GetProperty("content").EnumerateArray())
        {
            string? type = block.GetProperty("type").GetString();

            if (type == "text")
            {
                text.Append(block.GetProperty("text").GetString());
            }
            else if (type == "tool_use")
            {
                toolCalls.Add(new ChatToolCall
                {
                    Id = block.GetProperty("id").GetString() ?? string.Empty,
                    Name = block.GetProperty("name").GetString() ?? string.Empty,
                    Arguments = block.GetProperty("input").Clone()
                });
            }
        }

        return new ChatModelResponse
        {
            Text = text.Length > 0 ? text.ToString() : null,
            ToolCalls = toolCalls
        };
    }


    /// <summary>
    /// Claude krever at verktøysvar sendes som "tool_result"-blokker
    /// i en brukermelding, rett etter meldingen med "tool_use".
    /// </summary>
    private static JsonArray BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new JsonArray();
        JsonArray? pendingToolResults = null;

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                pendingToolResults ??= new JsonArray();

                pendingToolResults.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = message.ToolCallId,
                    ["content"] = message.Text ?? string.Empty
                });

                continue;
            }

            if (pendingToolResults is not null)
            {
                result.Add(new JsonObject { ["role"] = "user", ["content"] = pendingToolResults });
                pendingToolResults = null;
            }

            if (message.Role == ChatRole.User)
            {
                result.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = message.Text ?? string.Empty
                });
            }
            else
            {
                var content = new JsonArray();

                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = message.Text });
                }

                foreach (ChatToolCall call in message.ToolCalls)
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = JsonNode.Parse(call.Arguments.GetRawText())
                    });
                }

                if (content.Count > 0)
                {
                    result.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                }
            }
        }

        if (pendingToolResults is not null)
        {
            result.Add(new JsonObject { ["role"] = "user", ["content"] = pendingToolResults });
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
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.Parameters.DeepClone()
            });
        }

        return result;
    }
}
