using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Commonroom.ReferenceApi.Tests;

public sealed class BadRequestTests(TestApp app) : IClassFixture<TestApp>
{
    private static ByteArrayContent JsonBytes(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    /// <summary>"Qual è ..." encoded as Windows-1252: 0xE8 is not valid UTF-8.</summary>
    private static byte[] NonUtf8Body() =>
        [.. Encoding.ASCII.GetBytes("{\"content\":\"Qual "), 0xE8, .. Encoding.ASCII.GetBytes(" la capitale?\"}")];

    private static async Task AssertBadRequest(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("bad_request", body["code"]!.GetValue<string>());
        Assert.Equal(400, body["status"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(body["correlation_id"]?.GetValue<string>()));
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Non_utf8_body_on_create_returns_400_bad_request()
    {
        var client = app.ClientFor(Guid.NewGuid().ToString());
        await AssertBadRequest(await client.PostAsync("/conversations", JsonBytes(NonUtf8Body())));
    }

    [Fact]
    public async Task Non_utf8_body_on_send_message_returns_400_bad_request()
    {
        var client = app.ClientFor(Guid.NewGuid().ToString());
        var created = await client.PostAsJsonAsync("/conversations", new { content = "ciao" });
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>())!["conversation"]!["id"]!.GetValue<string>();

        await AssertBadRequest(await client.PostAsync($"/conversations/{id}/messages", JsonBytes(NonUtf8Body())));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"content\": 42}")]
    [InlineData("[]")]
    public async Task Malformed_json_returns_400_bad_request(string body)
    {
        var client = app.ClientFor(Guid.NewGuid().ToString());
        await AssertBadRequest(await client.PostAsync("/conversations", JsonBytes(Encoding.UTF8.GetBytes(body))));
    }
}
