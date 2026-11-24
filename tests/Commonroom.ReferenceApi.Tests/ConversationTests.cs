using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Commonroom.ReferenceApi.Engine;

namespace Commonroom.ReferenceApi.Tests;

public sealed class ConversationTests(TestApp app) : IClassFixture<TestApp>
{
    private static string NewSub() => Guid.NewGuid().ToString();

    private static async Task<JsonObject> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    private static async Task<string> CreateConversation(HttpClient client, string content, string? vectorStoreId = TestApp.AllowedStore)
    {
        var response = await client.PostAsJsonAsync("/conversations", new { content, metadata = new { vector_store_id = vectorStoreId } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response))["conversation"]!["id"]!.GetValue<string>();
    }

    // Acceptance 3
    [Fact]
    public async Task Post_conversation_creates_user_step_1_and_assistant_step_2_with_sources()
    {
        var client = app.ClientFor(NewSub());
        var text = "Quanti giorni di ferie ho? " + new string('x', 80);

        var response = await client.PostAsJsonAsync("/conversations",
            new { content = text, metadata = new { vector_store_id = TestApp.AllowedStore, project = "alpha" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var body = await Json(response);

        var conversation = body["conversation"]!.AsObject();
        Assert.Equal(text[..60], conversation["title"]!.GetValue<string>());
        Assert.Equal(TestApp.AllowedStore, conversation["metadata"]!["vector_store_id"]!.GetValue<string>());
        Assert.Equal("alpha", conversation["metadata"]!["project"]!.GetValue<string>());

        var messages = body["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal(1, messages[0]!["conversation_step"]!.GetValue<int>());
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal(text, messages[0]!["content"]!.GetValue<string>());
        Assert.Equal(2, messages[1]!["conversation_step"]!.GetValue<int>());
        Assert.Equal("assistant", messages[1]!["role"]!.GetValue<string>());
        Assert.Equal($"echo: {text}", messages[1]!["content"]!.GetValue<string>());
        Assert.True(messages[1]!["feedbackNeeded"]!.GetValue<bool>());

        var sources = messages[1]!["metadata"]!["source_documents"]!.AsArray();
        Assert.Single(sources);
        Assert.False(string.IsNullOrEmpty(sources[0]!["page_content"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(sources[0]!["metadata"]!["document"]!.GetValue<string>()));

        var id = conversation["id"]!.GetValue<string>();
        var list = await Json(await client.GetAsync($"/conversations/{id}/messages"));
        Assert.Equal(2, list["total"]!.GetValue<int>());

        var docs = (await client.GetFromJsonAsync<JsonArray>($"/conversations/{id}/documents"))!;
        Assert.Single(docs);
        var doc = await client.GetAsync($"/conversations/{id}/documents/{FakeChatEngine.FakeDocumentId}");
        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/conversations/{id}/documents/missing")).StatusCode);
    }

    // Acceptance 4
    [Fact]
    public async Task Other_user_gets_404_on_get_patch_delete_and_post_messages()
    {
        var owner = app.ClientFor(NewSub());
        var intruder = app.ClientFor(NewSub());
        var id = await CreateConversation(owner, "conversazione privata");

        await AssertNotFound(await intruder.GetAsync($"/conversations/{id}"));
        await AssertNotFound(await intruder.PatchAsJsonAsync($"/conversations/{id}", new { title = "rubata" }));
        await AssertNotFound(await intruder.DeleteAsync($"/conversations/{id}"));
        await AssertNotFound(await intruder.PostAsJsonAsync($"/conversations/{id}/messages", new { content = "ciao" }));
        await AssertNotFound(await intruder.GetAsync($"/conversations/{id}/messages"));

        var intruderList = await Json(await intruder.GetAsync("/conversations"));
        Assert.Equal(0, intruderList["total"]!.GetValue<int>());

        var mine = await owner.GetAsync($"/conversations/{id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("conversazione privata", (await Json(mine))["title"]!.GetValue<string>());
    }

    private static async Task AssertNotFound(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("not_found", (await Json(response))["code"]!.GetValue<string>());
    }

    // Acceptance 5
    [Fact]
    public async Task Vector_store_not_in_allowed_list_returns_403_vector_store_forbidden()
    {
        var client = app.ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/conversations",
            new { content = "ciao", metadata = new { vector_store_id = "vs_not_allowed" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("vector_store_forbidden", (await Json(response))["code"]!.GetValue<string>());
        Assert.Equal(0, (await Json(await client.GetAsync("/conversations")))["total"]!.GetValue<int>());
    }

    // Acceptance 6
    [Fact]
    public async Task Post_message_ignores_client_metadata_and_uses_stored_metadata()
    {
        var client = app.ClientFor(NewSub());
        var id = await CreateConversation(client, "prima domanda", TestApp.AllowedStore);
        var text = $"seconda domanda {Guid.NewGuid():N}";

        // Both a forbidden and a different allowed store must be ignored on an existing conversation.
        var response = await client.PostAsJsonAsync($"/conversations/{id}/messages",
            new { content = text, metadata = new { vector_store_id = "vs_not_allowed" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestApp.AllowedStore, app.Engine.LastVectorStoreByText[text]);

        var text2 = $"terza domanda {Guid.NewGuid():N}";
        var response2 = await client.PostAsJsonAsync($"/conversations/{id}/messages",
            new { content = text2, metadata = new { vector_store_id = TestApp.OtherAllowedStore } });
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(TestApp.AllowedStore, app.Engine.LastVectorStoreByText[text2]);

        var assistant = await Json(response2);
        Assert.Equal("assistant", assistant["role"]!.GetValue<string>());
        Assert.Equal(6, assistant["conversation_step"]!.GetValue<int>());
        Assert.True(assistant["feedbackNeeded"]!.GetValue<bool>());

        var conversation = await Json(await client.GetAsync($"/conversations/{id}"));
        Assert.Equal(TestApp.AllowedStore, conversation["metadata"]!["vector_store_id"]!.GetValue<string>());
    }

    // Acceptance 7
    [Fact]
    public async Task Feedback_on_user_step_is_400_and_on_assistant_step_is_204()
    {
        var client = app.ClientFor(NewSub());
        var id = await CreateConversation(client, "domanda per feedback");

        var onUser = await client.PostAsJsonAsync($"/conversations/{id}/messages/1/feedback", new { rating = "up" });
        Assert.Equal(HttpStatusCode.BadRequest, onUser.StatusCode);
        Assert.Equal("bad_request", (await Json(onUser))["code"]!.GetValue<string>());

        var onAssistant = await client.PostAsJsonAsync($"/conversations/{id}/messages/2/feedback", new { rating = "down", comment = "non preciso" });
        Assert.Equal(HttpStatusCode.NoContent, onAssistant.StatusCode);

        var badRating = await client.PostAsJsonAsync($"/conversations/{id}/messages/2/feedback", new { rating = "meh" });
        Assert.Equal(HttpStatusCode.BadRequest, badRating.StatusCode);

        var missingStep = await client.PostAsJsonAsync($"/conversations/{id}/messages/99/feedback", new { rating = "up" });
        Assert.Equal(HttpStatusCode.NotFound, missingStep.StatusCode);

        var messages = (await Json(await client.GetAsync($"/conversations/{id}/messages")))["items"]!.AsArray();
        var assistant = messages[1]!;
        Assert.False(assistant["feedbackNeeded"]!.GetValue<bool>());
        Assert.Equal("down", assistant["metadata"]!["feedback"]!["rating"]!.GetValue<string>());
        Assert.Equal("non preciso", assistant["metadata"]!["feedback"]!["comment"]!.GetValue<string>());
    }

    // Acceptance 8
    [Fact]
    public async Task List_is_paginated_and_page_size_above_100_is_clamped()
    {
        var client = app.ClientFor(NewSub());
        for (var i = 0; i < 3; i++)
        {
            await CreateConversation(client, $"conversazione {i}");
        }

        var clamped = await Json(await client.GetAsync("/conversations?page=1&page_size=500"));
        Assert.Equal(100, clamped["page_size"]!.GetValue<int>());
        Assert.Equal(1, clamped["page"]!.GetValue<int>());
        Assert.Equal(3, clamped["total"]!.GetValue<int>());
        Assert.Equal(3, clamped["items"]!.AsArray().Count);

        var defaults = await Json(await client.GetAsync("/conversations"));
        Assert.Equal(20, defaults["page_size"]!.GetValue<int>());

        var page2 = await Json(await client.GetAsync("/conversations?page=2&page_size=2"));
        Assert.Equal(2, page2["page"]!.GetValue<int>());
        Assert.Equal(2, page2["page_size"]!.GetValue<int>());
        Assert.Single(page2["items"]!.AsArray());
    }

    [Fact]
    public async Task Patch_renames_delete_soft_deletes_and_read_state_is_validated()
    {
        var client = app.ClientFor(NewSub());
        var id = await CreateConversation(client, "da rinominare");

        var patched = await client.PatchAsJsonAsync($"/conversations/{id}", new { title = "Nuovo titolo" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("Nuovo titolo", (await Json(patched))["title"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/conversations/{id}/read-state", new { step = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/conversations/{id}/read-state", new { step = 7 })).StatusCode);
        Assert.Equal(2, (await Json(await client.GetAsync($"/conversations/{id}")))["last_read_step"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/conversations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/conversations/{id}")).StatusCode);
    }

    [Fact]
    public async Task Auth_rules_from_step_1_still_apply()
    {
        var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/conversations")).StatusCode);

        var otherClient = app.ClientFor(NewSub(), clientId: "some-other-client");
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/conversations")).StatusCode);

        var noKey = app.CreateClient();
        noKey.DefaultRequestHeaders.Authorization = new("Bearer", TestApp.Token(NewSub()));
        var response = await noKey.GetAsync("/conversations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_api_key", (await Json(response))["code"]!.GetValue<string>());
    }
}
