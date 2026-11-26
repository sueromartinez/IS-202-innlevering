/*
    ChatModel.cs – felles format for AI-modellene

    Hva filen gjør: Vårt eget format for meldinger, verktøy og svar, og interfacet
    IChatModelClient.
    ClaudeChatClient og OllamaChatClient oversetter til og fra sitt API, så resten av Sami ikke
    vet hvilken modell som brukes. SamiOptions leses fra innstillingene (leverandør, modell,
    API-nøkkel).
*/

using System.Text.Json;
using System.Text.Json.Nodes;

namespace FirstWebAppInDocker.Services.Ai;

/*
    Felles, leverandøruavhengige typer for samtalen med språkmodellen.
    Ollama og Claude har litt ulike formater; klientene oversetter
    til og fra disse typene, slik at resten av koden er lik.
*/
public enum ChatRole
{
    User,
    Assistant,
    Tool
}

/*
    Et verktøykall modellen ønsker å gjøre, f.eks.
    find_nearby_resources med argumenter som JSON.
*/
public class ChatToolCall
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    // Argumentene som et JSON-objekt.
    public JsonElement Arguments { get; init; }
}

public class ChatMessage
{
    public ChatRole Role { get; init; }

    public string? Text { get; init; }

    // Verktøykall i en melding fra modellen (Role = Assistant).
    public List<ChatToolCall> ToolCalls { get; init; } = new();

    // For Role = Tool: hvilket kall dette er svaret på.
    public string? ToolCallId { get; init; }

    // For Role = Tool: navnet på verktøyet.
    public string? ToolName { get; init; }
}

/*
    Beskrivelse av et verktøy modellen kan bruke.
    Parameters er et JSON Schema-objekt.
*/
public class ChatToolDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public JsonObject Parameters { get; init; } = new();
}

public class ChatModelResponse
{
    public string? Text { get; init; }

    public List<ChatToolCall> ToolCalls { get; init; } = new();
}

/*
    Grensesnittet Sami bruker mot språkmodellen.
    Implementert av OllamaChatClient (lokal, gratis)
    og ClaudeChatClient (Claude API).
*/
public interface IChatModelClient
{
    Task<ChatModelResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken);
}

/*
    Kastes når språkmodellen ikke kan nås (nede, timeout, feil nøkkel).
    Controlleren gjør dette om til en forståelig melding til brukeren.
*/
public class ChatModelUnavailableException : Exception
{
    public ChatModelUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// Innstillinger for Sami, fra seksjonen "Sami" i appsettings.json.
public class SamiOptions
{
    // "Ollama" (standard, lokal) eller "Claude".
    public string Provider { get; set; } = "Ollama";

    // Maks antall runder med verktøykall per spørsmål.
    public int MaxToolRounds { get; set; } = 6;

    public OllamaSettings Ollama { get; set; } = new();

    public ClaudeSettings Claude { get; set; } = new();

    public class OllamaSettings
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";

        public string Model { get; set; } = "qwen2.5:7b";

        public int TimeoutSeconds { get; set; } = 180;
    }

    public class ClaudeSettings
    {
        /*
            Settes helst via miljøvariabelen ANTHROPIC_API_KEY
            eller user-secrets, aldri i koden eller i Git.
        */
        public string? ApiKey { get; set; }

        public string Model { get; set; } = "claude-haiku-5-5";

        public int MaxTokens { get; set; } = 1024;

        public int TimeoutSeconds { get; set; } = 60;
    }
}
