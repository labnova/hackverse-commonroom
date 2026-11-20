import { describe, expect, it, vi } from "vitest";
import { HttpClient, ProblemError } from "../src/core/http";
import type { Token } from "../src/types";
import { json, mockFetch, problem } from "./helpers";

const token = (access: string): Token => ({
  id_token: `id-${access}`,
  access_token: access,
  refresh_token: "refresh-1",
  expires_in: 3600,
  token_type: "Bearer",
});

describe("HttpClient headers", () => {
  it("adds Authorization, X-Api-Key, X-Correlation-Id, X-Session-Id, X-User-Identity and Content-Type", async () => {
    const { fetch, calls } = mockFetch(() => json(200, { ok: true }));
    const identity = { user_id: "u-1", user_name: "Niccolò Rossi", user_email: "n@commonroom.dev", user_role: "members" };
    const http = new HttpClient({
      basePath: "http://api.test/",
      apiKey: "dev-key",
      sessionId: "session-1",
      getToken: () => token("access-1"),
      userIdentity: identity,
      extraHeaders: { "X-Tenant": "alpha", "x-api-key": "must-not-override" },
      newId: () => "corr-1",
      fetch,
    });

    await http.post("/conversations", { content: "ciao" });

    expect(calls).toHaveLength(1);
    const [call] = calls;
    expect(call!.url).toBe("http://api.test/conversations");
    expect(call!.method).toBe("POST");
    expect(call!.headers["authorization"]).toBe("Bearer access-1");
    expect(call!.headers["x-api-key"]).toBe("dev-key");
    expect(call!.headers["x-correlation-id"]).toBe("corr-1");
    expect(call!.headers["x-session-id"]).toBe("session-1");
    expect(call!.headers["content-type"]).toBe("application/json; charset=utf-8");
    expect(call!.headers["x-tenant"]).toBe("alpha");
    // X-User-Identity is JSON, ASCII-only (valid header value) and round-trips to the identity.
    const raw = call!.headers["x-user-identity"]!;
    expect(raw).toMatch(/^[\x20-\x7e]+$/);
    expect(JSON.parse(raw)).toEqual(identity);
    expect(call!.body).toEqual({ content: "ciao" });
  });

  it("uses a new UUID correlation id for every request", async () => {
    const { fetch, calls } = mockFetch(() => json(200, {}));
    const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => token("a"), fetch });

    await http.get("/conversations");
    await http.get("/conversations");

    const [a, b] = calls.map((c) => c.headers["x-correlation-id"]);
    expect(a).toMatch(/^[0-9a-f-]{36}$/);
    expect(b).toMatch(/^[0-9a-f-]{36}$/);
    expect(a).not.toBe(b);
  });

  it("omits Authorization when there is no token", async () => {
    const { fetch, calls } = mockFetch(() => json(200, {}));
    const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => undefined, fetch });
    await http.get("/me");
    expect(calls[0]!.headers["authorization"]).toBeUndefined();
  });
});

describe("HttpClient 401 handling", () => {
  it("on 401 calls refresh once and retries once with the new token", async () => {
    const { fetch, calls } = mockFetch((_, i) => (i === 0 ? problem(401, "unauthorized") : json(200, { id: "c1" })));
    const refresh = vi.fn(async () => token("access-2"));
    const onRefreshed = vi.fn();
    const http = new HttpClient({
      apiKey: "k",
      sessionId: "s",
      getToken: () => token("access-1"),
      refreshToken: refresh,
      onTokenRefreshed: onRefreshed,
      fetch,
    });

    const result = await http.get<{ id: string }>("/conversations/c1");

    expect(result).toEqual({ id: "c1" });
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(onRefreshed).toHaveBeenCalledWith(token("access-2"));
    expect(calls).toHaveLength(2);
    expect(calls[0]!.headers["authorization"]).toBe("Bearer access-1");
    expect(calls[1]!.headers["authorization"]).toBe("Bearer access-2");
  });

  it("does not loop: a second 401 after refresh is surfaced as a ProblemError", async () => {
    const { fetch, calls } = mockFetch(() => problem(401, "unauthorized", "corr-401"));
    const refresh = vi.fn(async () => token("access-2"));
    const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => token("access-1"), refreshToken: refresh, fetch });

    const error = await http.get("/me").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ProblemError);
    expect((error as ProblemError).status).toBe(401);
    expect((error as ProblemError).correlationId).toBe("corr-401");
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(calls).toHaveLength(2);
  });

  it("without refresh_token_callback a 401 is an error and is not retried", async () => {
    const { fetch, calls } = mockFetch(() => problem(401, "unauthorized"));
    const http = new HttpClient({ apiKey: "k", sessionId: "s", getToken: () => token("a"), fetch });

    await expect(http.get("/me")).rejects.toMatchObject({ status: 401, code: "unauthorized" });
    expect(calls).toHaveLength(1);
  });

  it("if refresh fails the original 401 problem is returned", async () => {
    const { fetch, calls } = mockFetch(() => problem(401, "unauthorized"));
    const http = new HttpClient({
      apiKey: "k",
      sessionId: "s",
      getToken: () => token("a"),
      refreshToken: () => Promise.reject(new Error("silent renew failed")),
      fetch,
    });

    await expect(http.get("/me")).rejects.toMatchObject({ status: 401 });
    expect(calls).toHaveLength(1);
  });
});
