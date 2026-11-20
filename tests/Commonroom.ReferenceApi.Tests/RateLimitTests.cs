using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Commonroom.ReferenceApi.Tests;

public sealed class RateLimitTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Post_21_within_an_hour_returns_429_rate_limited_with_retry_after()
    {
        var client = app.ClientFor(Guid.NewGuid().ToString());

        // 1 create + 19 messages = 20 engine calls: all allowed (default RateLimit:MessagesPerHour = 20).
        var created = await client.PostAsJsonAsync("/conversations", new { content = "messaggio 1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["conversation"]!["id"]!.GetValue<string>();
        for (var i = 2; i <= 20; i++)
        {
            var ok = await client.PostAsJsonAsync($"/conversations/{id}/messages", new { content = $"messaggio {i}" });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await client.PostAsJsonAsync($"/conversations/{id}/messages", new { content = "messaggio 21" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.True(limited.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        var body = (await limited.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("rate_limited", body["code"]!.GetValue<string>());
        Assert.Equal(429, body["status"]!.GetValue<int>());

        // Creating a new conversation is limited too; reads are not.
        var limitedCreate = await client.PostAsJsonAsync("/conversations", new { content = "altra" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedCreate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/conversations/{id}/messages")).StatusCode);

        // The limit is per user (token sub).
        var other = app.ClientFor(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/conversations", new { content = "ciao" })).StatusCode);
    }
}
