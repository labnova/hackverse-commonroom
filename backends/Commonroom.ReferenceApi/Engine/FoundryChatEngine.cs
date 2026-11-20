using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Responses;

namespace Commonroom.ReferenceApi.Engine;

/// <summary>
/// Azure AI Foundry engine: Responses API on the project's OpenAI endpoint with the file_search tool
/// bound to the conversation's vector store. Authenticates only with DefaultAzureCredential.
/// </summary>
public sealed class FoundryChatEngine : IChatEngine
{
    public const string SystemPrompt = "Rispondi solo in base ai documenti; se non trovi la risposta dillo.";
    private const int MaxQuoteLength = 500;

    private readonly ProjectResponsesClient _responses;
    private readonly string _model;
    private readonly int _maxOutputTokens;

    public FoundryChatEngine(FoundryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectEndpoint))
        {
            throw new InvalidOperationException("Foundry:ProjectEndpoint is required when Chat:Engine=Foundry.");
        }
        if (string.IsNullOrWhiteSpace(options.ModelDeployment))
        {
            throw new InvalidOperationException("Foundry:ModelDeployment is required when Chat:Engine=Foundry.");
        }

        _model = options.ModelDeployment;
        _maxOutputTokens = options.MaxOutputTokens > 0 ? options.MaxOutputTokens : FoundryOptions.DefaultMaxOutputTokens;
        var project = new AIProjectClient(new Uri(options.ProjectEndpoint), new DefaultAzureCredential());
        _responses = project.ProjectOpenAIClient.GetProjectResponsesClient();
    }

    public async Task<ChatAnswer> Answer(IReadOnlyList<ChatTurn> history, string userText, string? vectorStoreId, CancellationToken ct)
    {
        var options = new CreateResponseOptions
        {
            Model = _model,
            Instructions = SystemPrompt,
            StoredOutputEnabled = false,
            MaxOutputTokenCount = _maxOutputTokens,
        };

        foreach (var turn in history)
        {
            options.InputItems.Add(turn.Role == "assistant"
                ? ResponseItem.CreateAssistantMessageItem(turn.Content, Array.Empty<ResponseMessageAnnotation>())
                : ResponseItem.CreateUserMessageItem(turn.Content));
        }
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(userText));

        if (!string.IsNullOrEmpty(vectorStoreId))
        {
            options.Tools.Add(new FileSearchTool(new[] { vectorStoreId }));
            options.IncludedProperties.Add(IncludedResponseProperty.FileSearchCallResults);
        }

        ResponseResult result = (await _responses.CreateResponseAsync(options, ct)).Value;

        // Quotes come from the file_search call results (included above), keyed by file id.
        var quotes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in result.OutputItems.OfType<FileSearchCallResponseItem>())
        {
            foreach (var hit in call.Results ?? [])
            {
                if (hit.FileId is { } id && hit.Text is { } text && !quotes.ContainsKey(id))
                {
                    quotes[id] = text.Length > MaxQuoteLength ? text[..MaxQuoteLength] : text;
                }
            }
        }

        var docs = new List<SourceDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in result.OutputItems.OfType<MessageResponseItem>())
        {
            foreach (var part in message.Content)
            {
                foreach (var citation in (part.OutputTextAnnotations ?? []).OfType<FileCitationMessageAnnotation>())
                {
                    if (citation.FileId is { } fileId && seen.Add(fileId))
                    {
                        docs.Add(new SourceDocument(fileId, citation.Filename ?? fileId, quotes.GetValueOrDefault(fileId)));
                    }
                }
            }
        }

        var tokens = result.Usage is { } u ? new TokenUsage(u.InputTokenCount, u.OutputTokenCount, u.TotalTokenCount) : null;
        return new ChatAnswer(result.GetOutputText() ?? string.Empty, docs, tokens);
    }
}
