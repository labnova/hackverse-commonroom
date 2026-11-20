import { vi } from "vitest";

export interface Call {
  url: string;
  method: string;
  headers: Record<string, string>;
  body: unknown;
}

export type Handler = (call: Call, index: number) => Response | Promise<Response>;

/** fetch mock that records calls (header names lower-cased) and answers via handler. */
export function mockFetch(handler: Handler) {
  const calls: Call[] = [];
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const headers: Record<string, string> = {};
    new Headers(init?.headers).forEach((value, key) => {
      headers[key] = value;
    });
    const call: Call = {
      url: String(input),
      method: init?.method ?? "GET",
      headers,
      body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined,
    };
    calls.push(call);
    return handler(call, calls.length - 1);
  });
  return { fetch: fn as unknown as typeof fetch, calls, fn };
}

export function json(status: number, body?: unknown, headers: Record<string, string> = {}): Response {
  if (status === 204) return new Response(null, { status, headers });
  return new Response(JSON.stringify(body ?? {}), {
    status,
    headers: { "Content-Type": status >= 400 ? "application/problem+json" : "application/json", ...headers },
  });
}

export const problem = (status: number, code: string, correlation = "srv-corr-1") =>
  json(status, { type: `https://commonroom.example/problems/${code}`, title: code, status, code, correlation_id: correlation });
