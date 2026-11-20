import { afterEach, describe, expect, it, vi } from "vitest";
import { HttpClient } from "../src/core/http";
import { ChatSession } from "../src/core/session";
import { createTransport } from "../src/core/transport";
import type { Conversation, ConversationMessage } from "../src/types";
import { json, mockFetch, type Call } from "./helpers";

const configMetadata = { vector_store_id: "vs_config", project: "alpha" };

const conversation = (metadata: Conversation["metadata"]): Conversation => ({
  id: "c1",
  title: "Ferie",
  created_at: "2026-11-20T09:00:00Z",
  updated_at: "2026-11-20T09:00:00Z",
  last_read_step: 2,
  metadata,
});

const message = (step: number, role: "user" | "assistant", content: string): ConversationMessage => ({
  session_id: "s",
  conversation_id: "c1",
  correlation_id: `corr-${step}`,
  timestamp: "2026-11-20T09:00:00Z",
  conversation_step: step,
  role,
  content,
});

function setup(router: (call: Call) => Response) {
  const mock = mockFetch((call) => router(call));
  const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => ({ id_token: "", access_token: "a", refresh_token: "", expires_in: 3600, token_type: "Bearer" }), fetch: mock.fetch });
  const session = new ChatSession(createTransport("openapi", http), { sessionId: "s", metadata: configMetadata });
  return { session, calls: mock.calls };
}

const path = (call: Call) => new URL(call.url, "http://x").pathname;

afterEach(() => vi.restoreAllMocks());

describe("ChatSession metadata", () => {
  it("an existing conversation keeps the server metadata: config metadata is never sent nor applied", async () => {
    const server = conversation({ vector_store_id: "vs_server", owner_note: "kept" });
    const { session, calls } = setup((call) => {
      if (call.method === "GET" && path(call) === "/conversations/c1") return json(200, server);
      if (call.method === "GET" && path(call) === "/conversations/c1/messages")
        return json(200, { items: [message(1, "user", "ciao"), message(2, "assistant", "echo")], page: 1, page_size: 100, total: 2 });
      if (call.method === "POST" && path(call) === "/conversations/c1/messages") return json(200, message(4, "assistant", "risposta"));
      if (call.method === "PUT") return json(204);
      throw new Error(`unexpected ${call.method} ${call.url}`);
    });

    await session.open("c1");
    expect(session.getState().conversation?.metadata).toEqual({ vector_store_id: "vs_server", owner_note: "kept" });

    const ok = await session.send("seconda domanda");
    expect(ok).toBe(true);

    const post = calls.find((c) => c.method === "POST")!;
    expect(post.body).toEqual({ content: "seconda domanda" });
    expect(post.body).not.toHaveProperty("metadata");
    expect(calls.some((c) => c.method === "POST" && path(c) === "/conversations")).toBe(false);

    const state = session.getState();
    expect(state.conversation?.metadata).toEqual({ vector_store_id: "vs_server", owner_note: "kept" });
    expect(state.messages.map((m) => [m.conversation_step, m.role])).toEqual([
      [1, "user"],
      [2, "assistant"],
      [3, "user"],
      [4, "assistant"],
    ]);
  });

  it("a new conversation is created with the metadata from the configuration", async () => {
    const created = conversation({ vector_store_id: "vs_config", project: "alpha" });
    const { session, calls } = setup((call) => {
      if (call.method === "POST" && path(call) === "/conversations")
        return json(201, { conversation: created, messages: [message(1, "user", "ciao"), message(2, "assistant", "echo")] });
      if (call.method === "PUT") return json(204);
      throw new Error(`unexpected ${call.method} ${call.url}`);
    });

    await session.send("ciao");

    const post = calls.find((c) => c.method === "POST")!;
    expect(post.body).toEqual({ content: "ciao", metadata: configMetadata });
    expect(session.getState().messages).toHaveLength(2);
    expect(session.getState().conversation?.id).toBe("c1");
  });

  it("a failed send surfaces the problem and removes the optimistic message", async () => {
    const { session } = setup(() => json(403, { title: "Vector store not allowed", status: 403, code: "vector_store_forbidden", correlation_id: "corr-403" }));

    const ok = await session.send("ciao");

    expect(ok).toBe(false);
    const state = session.getState();
    expect(state.messages).toHaveLength(0);
    expect(state.error).toMatchObject({ status: 403, code: "vector_store_forbidden", correlation_id: "corr-403" });
  });
});

describe("transport selection", () => {
  it("sse and ws fall back to openapi with a warning", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => undefined });
    expect(createTransport("sse", http).provider).toBe("openapi");
    expect(createTransport("ws", http).provider).toBe("openapi");
    expect(createTransport("openapi", http).supportsStop).toBe(false);
    expect(warn).toHaveBeenCalledTimes(2);
  });
});
