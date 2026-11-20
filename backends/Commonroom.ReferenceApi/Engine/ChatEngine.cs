namespace Commonroom.ReferenceApi.Engine;

/// <summary>One previous turn of the conversation passed to the engine (user or assistant only).</summary>
public sealed record ChatTurn(string Role, string Content);

/// <summary>A document cited by the engine.</summary>
public sealed record SourceDocument(string Id, string Name, string? Quote);

public sealed record TokenUsage(int Prompt, int Completion, int Total);

public sealed record ChatAnswer(string Content, IReadOnlyList<SourceDocument> SourceDocuments, TokenUsage? Tokens);

public interface IChatEngine
{
    Task<ChatAnswer> Answer(IReadOnlyList<ChatTurn> history, string userText, string? vectorStoreId, CancellationToken ct);
}

public sealed class FoundryOptions
{
    public string ProjectEndpoint { get; set; } = string.Empty;
    public string ModelDeployment { get; set; } = string.Empty;
    public const int DefaultMaxOutputTokens = 600;
    /// <summary>max_output_tokens sent to the Responses API.</summary>
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;
}

/// <summary>Deterministic engine for local runs and tests: no network, no credentials.</summary>
public sealed class FakeChatEngine : IChatEngine
{
    public const string FakeDocumentId = "fake-doc-1";
    public const string FakeDocumentName = "commonroom-handbook.md";

    public Task<ChatAnswer> Answer(IReadOnlyList<ChatTurn> history, string userText, string? vectorStoreId, CancellationToken ct)
    {
        var content = $"echo: {userText}";
        var docs = new[]
        {
            new SourceDocument(FakeDocumentId, FakeDocumentName, "Commonroom Labs: documento di esempio usato dal FakeChatEngine."),
        };
        var tokens = new TokenUsage(userText.Length, content.Length, userText.Length + content.Length);
        return Task.FromResult(new ChatAnswer(content, docs, tokens));
    }
}
