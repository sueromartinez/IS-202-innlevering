using System.Text.Json;
using System.Text.Json.Nodes;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// Felles, leverandøruavhengige typer for samtalen med språkmodellen.
/// Ollama og Claude har litt ulike formater; klientene oversetter
/// til og fra disse typene, slik at resten av koden er lik.
/// </summary>
public enum ChatRole
{
    User,
    Assistant,
    Tool
}


/// <summary>
/// Et verktøykall modellen ønsker å gjøre, f.eks.
/// find_nearby_resources med argumenter som JSON.
/// </summary>
public class ChatToolCall
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Argumentene som et JSON-objekt.
    /// </summary>
    public JsonElement Arguments { get; init; }
}


public class ChatMessage
{
    public ChatRole Role { get; init; }

    public string? Text { get; init; }

    /// <summary>
    /// Verktøykall i en melding fra modellen (Role = Assistant).
    /// </summary>
    public List<ChatToolCall> ToolCalls { get; init; } = new();

    /// <summary>
    /// For Role = Tool: hvilket kall dette er svaret på.
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// For Role = Tool: navnet på verktøyet.
    /// </summary>
    public string? ToolName { get; init; }
}


/// <summary>
/// Beskrivelse av et verktøy modellen kan bruke.
/// Parameters er et JSON Schema-objekt.
/// </summary>
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


/// <summary>
/// Grensesnittet Sami bruker mot språkmodellen.
/// Implementert av OllamaChatClient (lokal, gratis)
/// og ClaudeChatClient (Claude API).
/// </summary>
public interface IChatModelClient
{
    Task<ChatModelResponse> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken
    );
}


/// <summary>
/// Kastes når språkmodellen ikke kan nås (nede, timeout, feil nøkkel).
/// Controlleren gjør dette om til en forståelig melding til brukeren.
/// </summary>
public class ChatModelUnavailableException : Exception
{
    public ChatModelUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}


/// <summary>
/// Innstillinger for Sami, fra seksjonen "Sami" i appsettings.json.
/// </summary>
public class SamiOptions
{
    /// <summary>
    /// "Ollama" (standard, lokal) eller "Claude".
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>
    /// Maks antall runder med verktøykall per spørsmål.
    /// </summary>
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
        /// <summary>
        /// Settes helst via miljøvariabelen ANTHROPIC_API_KEY
        /// eller user-secrets, aldri i koden eller i Git.
        /// </summary>
        public string? ApiKey { get; set; }

        public string Model { get; set; } = "claude-haiku-5-5";

        public int MaxTokens { get; set; } = 1024;

        public int TimeoutSeconds { get; set; } = 60;
    }
}
