using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Commonroom.ReferenceApi.Data;
using Commonroom.ReferenceApi.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Commonroom.ReferenceApi.Api;

public static class ConversationEndpoints
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int TitleLength = 60;
    public const int MaxContentLength = 16000;
    public const int MaxTitleLength = 200;
    public const int MaxCommentLength = 2000;
    private const int HistoryTurns = 20;

    private static readonly JsonSerializerOptions StoreJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static IEndpointRouteBuilder MapConversations(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/conversations").RequireAuthorization();

        g.MapPost("", Create).RequireRateLimiting(MessageRateLimit.PolicyName);
        g.MapGet("", List);
        g.MapGet("/{id}", Get);
        g.MapPatch("/{id}", Update);
        g.MapDelete("/{id}", Delete);
        g.MapGet("/{id}/messages", ListMessages);
        g.MapPost("/{id}/messages", SendMessage).RequireRateLimiting(MessageRateLimit.PolicyName);
        g.MapPut("/{id}/read-state", PutReadState);
        g.MapGet("/{id}/documents", ListDocuments);
        g.MapGet("/{id}/documents/{docId}", GetDocument);
        g.MapPost("/{id}/messages/{step:int}/feedback", PostFeedback);

        return app;
    }

    // POST /conversations
    private static async Task<IResult> Create(
        HttpContext ctx, CreateConversationRequest req, ChatDb db, IChatEngine engine,
        IOptionsMonitor<VectorStoreOptions> stores, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        if (ValidateContent(ctx, req.Content) is { } invalid) return invalid;

        var metadata = req.Metadata?.DeepClone().AsObject() ?? new JsonObject();
        if (!TryReadVectorStoreId(metadata, out var vectorStoreId))
        {
            return BadRequest(ctx, "metadata.vector_store_id must be a string or null.");
        }
        if (Forbidden(ctx, vectorStoreId, stores.CurrentValue) is { } forbidden) return forbidden;
        metadata["vector_store_id"] = vectorStoreId;

        var content = req.Content!;
        var answer = await engine.Answer([], content, vectorStoreId, ct);

        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            OwnerSub = sub,
            Title = MakeTitle(content),
            MetadataJson = metadata.ToJsonString(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var user = NewMessage(ctx, conversation.Id, 1, "user", content, null, now);
        var assistant = NewMessage(ctx, conversation.Id, 2, "assistant", answer.Content, AnswerMetadata(answer), now);

        db.Conversations.Add(conversation);
        db.Messages.AddRange(user, assistant);
        await db.SaveChangesAsync(ct);

        var body = new CreateConversationResponse(ToDto(conversation), [ToDto(user, null), ToDto(assistant, null)]);
        return TypedResults.Created($"/conversations/{conversation.Id}", body);
    }

    // GET /conversations
    private static async Task<IResult> List(
        HttpContext ctx, ChatDb db, [FromQuery] int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var (p, size) = Paging(page, pageSize);

        var query = db.Conversations.AsNoTracking().Where(c => c.OwnerSub == sub && !c.Deleted);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);

        return TypedResults.Ok(new PageDto<ConversationDto>(items.Select(ToDto).ToList(), p, size, total));
    }

    // GET /conversations/{id}
    private static async Task<IResult> Get(HttpContext ctx, string id, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        return conversation is null ? NotFound(ctx) : TypedResults.Ok(ToDto(conversation));
    }

    // PATCH /conversations/{id}
    private static async Task<IResult> Update(HttpContext ctx, string id, UpdateConversationRequest req, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);

        var title = req.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
        {
            return BadRequest(ctx, $"title is required (1-{MaxTitleLength} characters).");
        }

        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(conversation));
    }

    // DELETE /conversations/{id} (soft delete)
    private static async Task<IResult> Delete(HttpContext ctx, string id, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);

        conversation.Deleted = true;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // GET /conversations/{id}/messages
    private static async Task<IResult> ListMessages(
        HttpContext ctx, string id, ChatDb db, [FromQuery] int? page, [FromQuery(Name = "page_size")] int? pageSize, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);
        var (p, size) = Paging(page, pageSize);

        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversation.Id);
        var total = await query.CountAsync(ct);
        var messages = await query.OrderBy(m => m.Step).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var steps = messages.Select(m => m.Step).ToList();
        var feedback = await db.Feedback.AsNoTracking()
            .Where(f => f.ConversationId == conversation.Id && steps.Contains(f.Step))
            .ToDictionaryAsync(f => f.Step, ct);

        var items = messages.Select(m => ToDto(m, feedback.GetValueOrDefault(m.Step))).ToList();
        return TypedResults.Ok(new PageDto<ConversationMessageDto>(items, p, size, total));
    }

    // POST /conversations/{id}/messages
    private static async Task<IResult> SendMessage(
        HttpContext ctx, string id, SendMessageRequest req, ChatDb db, IChatEngine engine,
        IOptionsMonitor<VectorStoreOptions> stores, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);
        if (ValidateContent(ctx, req.Content) is { } invalid) return invalid;

        // Always the stored metadata: req.Metadata is ignored on existing conversations.
        var stored = ParseObject(conversation.MetadataJson);
        TryReadVectorStoreId(stored, out var vectorStoreId);
        if (Forbidden(ctx, vectorStoreId, stores.CurrentValue) is { } forbidden) return forbidden;

        var previous = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.Step)
            .Take(HistoryTurns)
            .ToListAsync(ct);
        var lastStep = previous.Count == 0 ? 0 : previous[0].Step;
        var history = previous
            .Where(m => m.Role is "user" or "assistant")
            .OrderBy(m => m.Step)
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToList();

        var content = req.Content!;
        var answer = await engine.Answer(history, content, vectorStoreId, ct);

        var now = DateTime.UtcNow;
        var user = NewMessage(ctx, conversation.Id, lastStep + 1, "user", content, null, now);
        var assistant = NewMessage(ctx, conversation.Id, lastStep + 2, "assistant", answer.Content, AnswerMetadata(answer), now);
        db.Messages.AddRange(user, assistant);
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToDto(assistant, null));
    }

    // PUT /conversations/{id}/read-state
    private static async Task<IResult> PutReadState(HttpContext ctx, string id, ReadStateRequest req, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);

        var maxStep = await db.Messages.Where(m => m.ConversationId == conversation.Id).MaxAsync(m => (int?)m.Step, ct) ?? 0;
        if (req.Step is not { } step || step < 0 || step > maxStep)
        {
            return BadRequest(ctx, $"step must be between 0 and {maxStep}.");
        }

        conversation.LastReadStep = step;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // POST /conversations/{id}/messages/{step}/feedback
    private static async Task<IResult> PostFeedback(HttpContext ctx, string id, int step, FeedbackRequest req, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);

        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.ConversationId == conversation.Id && m.Step == step, ct);
        if (message is null) return NotFound(ctx, $"Step {step} not found.");
        if (message.Role != "assistant") return BadRequest(ctx, "Feedback is only accepted on assistant messages.");
        if (req.Rating is not ("up" or "down")) return BadRequest(ctx, "rating must be 'up' or 'down'.");
        if (req.Comment is { Length: > MaxCommentLength }) return BadRequest(ctx, $"comment exceeds {MaxCommentLength} characters.");

        var existing = await db.Feedback.FindAsync([conversation.Id, step], ct);
        if (existing is null)
        {
            db.Feedback.Add(new Feedback { ConversationId = conversation.Id, Step = step, Rating = req.Rating, Comment = req.Comment, CreatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Rating = req.Rating;
            existing.Comment = req.Comment;
            existing.CreatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // GET /conversations/{id}/documents
    private static async Task<IResult> ListDocuments(HttpContext ctx, string id, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);
        return TypedResults.Ok(await CollectDocuments(db, conversation.Id, ct));
    }

    // GET /conversations/{id}/documents/{docId}
    private static async Task<IResult> GetDocument(HttpContext ctx, string id, string docId, ChatDb db, CancellationToken ct)
    {
        if (Sub(ctx) is not { } sub) return Unauthorized(ctx);
        var conversation = await FindOwned(db, id, sub, ct);
        if (conversation is null) return NotFound(ctx);

        var doc = (await CollectDocuments(db, conversation.Id, ct)).FirstOrDefault(d => d.Id == docId);
        return doc is null ? NotFound(ctx, $"Document {docId} not found in this conversation.") : TypedResults.Ok(doc);
    }

    // ---- helpers ----

    private static string? Sub(HttpContext ctx) => ctx.User.FindFirstValue("sub") is { Length: > 0 } s ? s : null;

    private static async Task<Conversation?> FindOwned(ChatDb db, string id, string sub, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return null;
        // Ownership filter: another user's conversation is indistinguishable from a missing one (404).
        return await db.Conversations.FirstOrDefaultAsync(c => c.Id == guid && c.OwnerSub == sub && !c.Deleted, ct);
    }

    private static (int Page, int Size) Paging(int? page, int? pageSize) =>
        (Math.Clamp(page ?? 1, 1, 1_000_000), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    private static IResult? ValidateContent(HttpContext ctx, string? content) =>
        string.IsNullOrWhiteSpace(content) || content.Length > MaxContentLength
            ? BadRequest(ctx, $"content is required (1-{MaxContentLength} characters).")
            : null;

    private static bool TryReadVectorStoreId(JsonObject metadata, out string? vectorStoreId)
    {
        vectorStoreId = null;
        if (!metadata.TryGetPropertyValue("vector_store_id", out var node) || node is null) return true;
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            vectorStoreId = string.IsNullOrWhiteSpace(s) ? null : s;
            return true;
        }
        return false;
    }

    private static IResult? Forbidden(HttpContext ctx, string? vectorStoreId, VectorStoreOptions options) =>
        vectorStoreId is null || options.Allowed.Contains(vectorStoreId, StringComparer.Ordinal)
            ? null
            : Problems.Result(ctx, StatusCodes.Status403Forbidden, ErrorCodes.VectorStoreForbidden,
                "vector_store_id is not allowed for this project.");

    private static IResult Unauthorized(HttpContext ctx) =>
        Problems.Result(ctx, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Token has no sub claim.");

    private static IResult NotFound(HttpContext ctx, string? detail = null) =>
        Problems.Result(ctx, StatusCodes.Status404NotFound, ErrorCodes.NotFound, detail ?? "Conversation not found.");

    private static IResult BadRequest(HttpContext ctx, string detail) =>
        Problems.Result(ctx, StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, detail);

    private static string MakeTitle(string content)
    {
        var flat = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= TitleLength ? flat : flat[..TitleLength];
    }

    private static Message NewMessage(HttpContext ctx, Guid conversationId, int step, string role, string content, string? metadataJson, DateTime now) => new()
    {
        ConversationId = conversationId,
        Step = step,
        Role = role,
        Content = content,
        MetadataJson = metadataJson,
        CorrelationId = CorrelationId.Get(ctx),
        SessionId = SessionId(ctx),
        Timestamp = now,
    };

    private static string SessionId(HttpContext ctx)
    {
        var value = ctx.Request.Headers[Headers.SessionId].ToString();
        return value.Length is > 0 and <= 128 ? value : string.Empty;
    }

    private static string AnswerMetadata(ChatAnswer answer)
    {
        var meta = new JsonObject
        {
            ["source_documents"] = JsonSerializer.SerializeToNode(answer.SourceDocuments, StoreJson),
        };
        if (answer.Tokens is not null)
        {
            meta["tokens"] = JsonSerializer.SerializeToNode(answer.Tokens, StoreJson);
        }
        return meta.ToJsonString();
    }

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new JsonObject();
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static async Task<List<ConversationDocumentDto>> CollectDocuments(ChatDb db, Guid conversationId, CancellationToken ct)
    {
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Role == "assistant" && m.MetadataJson != null)
            .OrderBy(m => m.Step)
            .Select(m => new { m.Step, m.MetadataJson })
            .ToListAsync(ct);

        var docs = new Dictionary<string, ConversationDocumentDto>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (ParseObject(row.MetadataJson)["source_documents"] is not JsonArray list) continue;
            foreach (var item in list.OfType<JsonObject>())
            {
                // 0.3.0 shape: page_content + metadata{document, key}; fallback to 0.2.0 id/name/quote for stored messages.
                var meta = item["metadata"] as JsonObject;
                if ((meta?["key"] ?? item["id"])?.GetValue<string>() is not { } docId) continue;
                docs[docId] = docs.TryGetValue(docId, out var seen)
                    ? seen with { CitationCount = seen.CitationCount + 1 }
                    : new ConversationDocumentDto(docId, (meta?["document"] ?? item["name"])?.GetValue<string>() ?? docId,
                        (item["page_content"] ?? item["quote"])?.GetValue<string>(), row.Step, 1);
            }
        }
        return docs.Values.ToList();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ConversationDto ToDto(Conversation c) =>
        new(c.Id.ToString(), c.Title, Utc(c.CreatedAt), Utc(c.UpdatedAt), c.LastReadStep, ParseObject(c.MetadataJson));

    private static ConversationMessageDto ToDto(Message m, Feedback? feedback)
    {
        JsonObject? metadata = m.MetadataJson is null ? null : ParseObject(m.MetadataJson);
        if (feedback is not null)
        {
            metadata ??= new JsonObject();
            metadata["feedback"] = new JsonObject
            {
                ["rating"] = feedback.Rating,
                ["comment"] = feedback.Comment,
                ["created_at"] = Utc(feedback.CreatedAt),
            };
        }

        bool? feedbackNeeded = m.Role == "assistant" ? feedback is null : null;
        return new ConversationMessageDto(m.SessionId, m.ConversationId.ToString(), m.CorrelationId, Utc(m.Timestamp),
            m.Step, m.Role, m.Content, feedbackNeeded, metadata);
    }
}
