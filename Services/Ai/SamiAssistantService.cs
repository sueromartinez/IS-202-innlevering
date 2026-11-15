using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// Svaret Sami gir tilbake til chatten.
/// </summary>
public class SamiReply
{
    public string Text { get; init; } = string.Empty;

    public List<SamiSuggestion> Suggestions { get; init; } = new();

    public List<SamiResourceDraft> ResourceDrafts { get; init; } = new();
}


/// <summary>
/// Kjører samtalen med språkmodellen ("tool-loopen"):
///
///   1. Send samtalen + verktøyene til modellen.
///   2. Vil modellen bruke et verktøy? Kjør det i C# og send svaret tilbake.
///   3. Gjenta til modellen gir et ferdig svar (maks MaxToolRounds runder).
///
/// Hvert verktøykall logges, slik at man i etterkant kan se
/// hva Sami har slått opp og foreslått.
/// </summary>
public class SamiAssistantService
{
    private readonly IChatModelClient _model;

    private readonly SamiTools _tools;

    private readonly SamiOptions _options;

    private readonly ILogger<SamiAssistantService> _logger;


    public SamiAssistantService(
        IChatModelClient model,
        SamiTools tools,
        IOptions<SamiOptions> options,
        ILogger<SamiAssistantService> logger
    )
    {
        _model = model;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }


    public async Task<SamiReply> RunAsync(
        SamiContext context,
        List<ChatMessage> history,
        CancellationToken cancellationToken
    )
    {
        string systemPrompt = SamiPrompts.For(context);

        IReadOnlyList<ChatToolDefinition> tools = _tools.GetDefinitions(context.Role);

        var messages = new List<ChatMessage>(history);
        var output = new SamiTurnOutput();

        for (int round = 0; round < _options.MaxToolRounds; round++)
        {
            ChatModelResponse response =
                await _model.CompleteAsync(systemPrompt, messages, tools, cancellationToken);

            if (response.ToolCalls.Count == 0)
            {
                return new SamiReply
                {
                    Text = string.IsNullOrWhiteSpace(response.Text)
                        ? "Beklager, jeg fikk ikke til å lage et svar. Kan du prøve å spørre på en annen måte?"
                        : response.Text.Trim(),
                    Suggestions = output.Suggestions,
                    ResourceDrafts = output.ResourceDrafts
                };
            }

            messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = response.Text,
                ToolCalls = response.ToolCalls
            });

            foreach (ChatToolCall call in response.ToolCalls)
            {
                _logger.LogInformation(
                    "Sami-verktøy {Tool} for bruker {UserId} ({Role}) med {Arguments}",
                    call.Name,
                    context.UserId,
                    context.Role,
                    call.Arguments.ValueKind == JsonValueKind.Undefined ? "{}" : call.Arguments.GetRawText());

                string result;

                try
                {
                    result = await _tools.ExecuteAsync(call, context, output, cancellationToken);
                }
                catch (SamiToolException exception)
                {
                    _logger.LogError(exception.InnerException, "Sami-verktøyet {Tool} feilet", exception.ToolName);

                    result = """{ "error": "Verktøyet feilet. Si til brukeren at du ikke fikk hentet dataene akkurat nå." }""";
                }

                messages.Add(new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Text = result
                });
            }
        }

        _logger.LogWarning("Sami brukte for mange verktøyrunder for bruker {UserId}", context.UserId);

        return new SamiReply
        {
            Text = "Beklager, dette ble for mange steg for meg. Prøv å dele spørsmålet opp i mindre deler.",
            Suggestions = output.Suggestions,
            ResourceDrafts = output.ResourceDrafts
        };
    }
}
