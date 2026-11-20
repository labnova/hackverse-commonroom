using Microsoft.AspNetCore.Diagnostics;

namespace Commonroom.ReferenceApi;

/// <summary>
/// Maps BadHttpRequestException (malformed JSON, body not decodable as UTF-8, oversized body, ...)
/// to a 4xx application/problem+json with code bad_request instead of a 500.
/// </summary>
public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        var status = bad.StatusCode is >= 400 and < 500 ? bad.StatusCode : StatusCodes.Status400BadRequest;
        logger.LogWarning("Bad request {Method} {Path}: {Reason}", ctx.Request.Method, ctx.Request.Path,
            bad.InnerException?.Message ?? bad.Message);

        var detail = status == StatusCodes.Status400BadRequest
            ? "The request body is not valid UTF-8 JSON or does not match the expected schema."
            : bad.Message;
        await Problems.WriteAsync(ctx, status, ErrorCodes.BadRequest, detail);
        return true;
    }
}
