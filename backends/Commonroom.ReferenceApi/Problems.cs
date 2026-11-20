using Microsoft.AspNetCore.Mvc;

namespace Commonroom.ReferenceApi;

public static class ErrorCodes
{
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string InvalidApiKey = "invalid_api_key";
    public const string NotFound = "not_found";
    public const string NotImplemented = "not_implemented";
    public const string BadRequest = "bad_request";
    public const string VectorStoreForbidden = "vector_store_forbidden";
    public const string RateLimited = "rate_limited";
}

public static class Problems
{
    public const string ContentType = "application/problem+json";

    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        [ErrorCodes.Unauthorized] = "Unauthorized",
        [ErrorCodes.Forbidden] = "Forbidden",
        [ErrorCodes.InvalidApiKey] = "Invalid API key",
        [ErrorCodes.NotFound] = "Not found",
        [ErrorCodes.NotImplemented] = "Not implemented",
        [ErrorCodes.BadRequest] = "Bad request",
        [ErrorCodes.VectorStoreForbidden] = "Vector store not allowed",
        [ErrorCodes.RateLimited] = "Too many requests",
    };

    public static ProblemDetails Create(HttpContext ctx, int status, string code, string detail) => new()
    {
        Type = $"https://commonroom.example/problems/{code}",
        Title = Titles.GetValueOrDefault(code, code),
        Status = status,
        Detail = detail,
        Instance = ctx.Request.Path,
        Extensions =
        {
            ["code"] = code,
            ["correlation_id"] = CorrelationId.Get(ctx),
        },
    };

    public static Task WriteAsync(HttpContext ctx, int status, string code, string detail)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(Create(ctx, status, code, detail), options: null, contentType: ContentType);
    }

    public static IResult Result(HttpContext ctx, int status, string code, string detail) =>
        TypedResults.Json(Create(ctx, status, code, detail), options: null, contentType: ContentType, statusCode: status);

    public static IResult NotImplemented(HttpContext ctx) =>
        Result(ctx, StatusCodes.Status501NotImplemented, ErrorCodes.NotImplemented,
            $"{ctx.Request.Method} {ctx.Request.Path} is part of the contract but not implemented by the reference backend.");
}
