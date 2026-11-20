using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Commonroom.ReferenceApi;

/// <summary>
/// Per-user (token sub) limit on the endpoints that call the chat engine:
/// POST /conversations and POST /conversations/{id}/messages.
/// </summary>
public static class MessageRateLimit
{
    public const string PolicyName = "messages";
    public const int DefaultMessagesPerHour = 20;

    public static IServiceCollection AddMessageRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var perHour = configuration.GetValue("RateLimit:MessagesPerHour", DefaultMessagesPerHour);
        if (perHour < 1)
        {
            throw new InvalidOperationException("RateLimit:MessagesPerHour must be >= 1.");
        }

        return services.AddRateLimiter(o =>
        {
            o.AddPolicy(PolicyName, ctx => RateLimitPartition.GetSlidingWindowLimiter(
                ctx.User.FindFirstValue("sub") ?? "anonymous",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = perHour,
                    Window = TimeSpan.FromHours(1),
                    SegmentsPerWindow = 12,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            o.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? (int)Math.Ceiling(wait.TotalSeconds)
                    : 300;
                http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await Problems.WriteAsync(http, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited,
                    $"Message limit reached ({perHour} per hour). Retry after {retryAfter} seconds.");
            };
        });
    }
}
