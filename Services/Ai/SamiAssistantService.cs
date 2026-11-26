/*
    SamiAssistantService.cs – hjernen til Sami

    Hva filen gjør: Sami er en AI-assistent som kan bruke verktøy. Slik fungerer det:
    1) Vi sender reglene (systemprompt), samtalen og en liste over verktøy til AI-modellen.
    2) Modellen svarer enten med tekst (da er den ferdig), eller med "jeg vil bruke verktøy X".
    3) Vår kode kjører verktøyet mot vår database og sender resultatet tilbake til modellen.
    4) Dette gjentas til modellen svarer med tekst, maks 6 ganger.
    Hvorfor verktøy: modellen ser bare det den trenger, svarer med ekte tall fra databasen, og
    vi bestemmer i koden hva den får lov til.

    På nettsiden: Svarene i chatboblen, og kortene med forslag og ressursutkast.

    Oppgavekrav: 10, 19
*/

using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FirstWebAppInDocker.Services.Ai;

// Svaret Sami gir tilbake til chatten.
public class SamiReply
{
    public string Text { get; init; } = string.Empty;

    public List<SamiSuggestion> Suggestions { get; init; } = new();

    public List<SamiResourceDraft> ResourceDrafts { get; init; } = new();
}

/*
    Kjører samtalen med språkmodellen ("tool-loopen"):

      1. Send samtalen + verktøyene til modellen.
      2. Vil modellen bruke et verktøy? Kjør det i C# og send svaret tilbake.
      3. Gjenta til modellen gir et ferdig svar (maks MaxToolRounds runder).

    Hvert verktøykall logges, slik at man i etterkant kan se
    hva Sami har slått opp og foreslått.
*/
public class SamiAssistantService
{
    #region Felt og konstruktør

    private readonly IChatModelClient _chatModel;

    private readonly SamiTools _tools;

    private readonly SamiOptions _options;

    private readonly ILogger<SamiAssistantService> _logger;

    public SamiAssistantService(
        IChatModelClient model,
        SamiTools tools,
        IOptions<SamiOptions> options,
        ILogger<SamiAssistantService> logger)
    {
        _chatModel = model;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }

    #endregion

    #region Kjør Sami

    public async Task<SamiReply> RunAsync(
        SamiContext context,
        List<ChatMessage> history,
        CancellationToken cancellationToken)
    {
        // Reglene og verktøylisten er forskjellig for frivillig, offentlig aktør og admin.
        string systemPrompt = SamiPrompts.For(context);

        IReadOnlyList<ChatToolDefinition> tools = _tools.GetDefinitions(context.Role);

        List<ChatMessage> messages = new List<ChatMessage>(history);
        SamiTurnOutput output = new SamiTurnOutput();

        /*
            Løkken fra steg 2–4. Taket på antall runder hindrer at modellen går i ring og bruker
            opp penger.
        */
        for (int round = 0; round < _options.MaxToolRounds; round++)
        {
            ChatModelResponse response =
                await _chatModel.CompleteAsync(systemPrompt, messages, tools, cancellationToken);

            // Ingen verktøy = modellen er ferdig, og svaret går til brukeren.
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
                    /*
                        Vi kjører verktøyet og legger resultatet inn i samtalen, så modellen ser
                        det i neste runde.
                    */
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

    #endregion
}
