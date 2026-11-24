using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Commonroom.ReferenceApi.Api;

// Requests (snake_case via the global JSON naming policy).
public sealed record CreateConversationRequest(string? Content, JsonObject? Metadata);

public sealed record UpdateConversationRequest(string? Title);

/// <summary>metadata is accepted for compatibility but ignored: the stored conversation metadata always wins.</summary>
public sealed record SendMessageRequest(string? Content, JsonObject? Metadata);

public sealed record ReadStateRequest(int? Step);

public sealed record FeedbackRequest(string? Rating, string? Comment);

// Responses.
public sealed record ConversationDto(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int LastReadStep,
    JsonObject Metadata);

public sealed record ConversationMessageDto(
    string SessionId,
    string ConversationId,
    string CorrelationId,
    DateTimeOffset Timestamp,
    int ConversationStep,
    string Role,
    string Content,
    [property: JsonPropertyName("feedbackNeeded")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? FeedbackNeeded,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonObject? Metadata);

public sealed record CreateConversationResponse(ConversationDto Conversation, IReadOnlyList<ConversationMessageDto> Messages);

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record ConversationDocumentDto(string Id, string Name, string? PageContent, int FirstStep, int CitationCount);
