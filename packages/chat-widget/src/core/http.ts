import type { Problem, Token, UserIdentifier } from "../types";
import { asciiJson, uuid } from "./ids";

export interface HttpClientOptions {
  basePath?: string;
  apiKey: string;
  sessionId: string;
  getToken: () => Token | null | undefined;
  refreshToken?: () => Promise<Token>;
  onTokenRefreshed?: (token: Token) => void;
  userIdentity?: UserIdentifier;
  extraHeaders?: Record<string, string>;
  correlationPrefix?: string;
  fetch?: typeof fetch;
  newId?: () => string;
}

const RESERVED = new Set([
  "authorization",
  "x-api-key",
  "x-correlation-id",
  "x-session-id",
  "x-user-identity",
  "content-type",
  "accept",
]);

/** Error carrying an application/problem+json payload (or a synthesized one for network failures). */
export class ProblemError extends Error {
  readonly problem: Problem;

  constructor(problem: Problem) {
    super(problem.detail ? `${problem.title}: ${problem.detail}` : problem.title);
    this.name = "ProblemError";
    this.problem = problem;
  }

  get status(): number {
    return this.problem.status;
  }

  get code(): string | undefined {
    return this.problem.code;
  }

  get correlationId(): string | undefined {
    return this.problem.correlation_id;
  }

  static async fromResponse(res: Response, requestCorrelationId: string): Promise<ProblemError> {
    const correlation = res.headers.get("X-Correlation-Id") ?? requestCorrelationId;
    let body: Partial<Problem> = {};
    try {
      const text = await res.text();
      if (text) body = JSON.parse(text) as Partial<Problem>;
    } catch {
      // not JSON: keep the defaults below
    }
    return new ProblemError({
      ...body,
      title: body.title ?? (res.statusText || `HTTP ${res.status}`),
      status: body.status ?? res.status,
      correlation_id: body.correlation_id ?? correlation,
    });
  }

  static network(error: unknown, correlationId: string): ProblemError {
    return new ProblemError({
      title: "Network error",
      status: 0,
      detail: error instanceof Error ? error.message : String(error),
      code: "network_error",
      correlation_id: correlationId,
    });
  }
}

export function toProblem(error: unknown): Problem {
  if (error instanceof ProblemError) return error.problem;
  return { title: "Unexpected error", status: 0, detail: error instanceof Error ? error.message : String(error) };
}

/**
 * fetch wrapper with the Commonroom headers. On 401 it asks refreshToken() once and retries once.
 */
export class HttpClient {
  private readonly o: HttpClientOptions;
  private readonly fetchImpl: typeof fetch;
  private readonly newId: () => string;
  private readonly base: string;

  constructor(options: HttpClientOptions) {
    this.o = options;
    this.fetchImpl = options.fetch ?? ((input, init) => globalThis.fetch(input, init));
    this.newId = options.newId ?? uuid;
    this.base = (options.basePath ?? "").replace(/\/+$/, "");
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>("GET", path);
  }

  post<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("POST", path, body);
  }

  put<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("PUT", path, body);
  }

  patch<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("PATCH", path, body);
  }

  delete<T>(path: string): Promise<T> {
    return this.request<T>("DELETE", path);
  }

  async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const correlationId = this.correlationId();
    const payload = body === undefined ? undefined : JSON.stringify(body);
    const send = (token: Token | null | undefined) =>
      this.fetchImpl(`${this.base}${path}`, { method, headers: this.headers(token, correlationId), body: payload });

    let res: Response;
    try {
      res = await send(this.o.getToken());
    } catch (error) {
      throw ProblemError.network(error, correlationId);
    }

    if (res.status === 401 && this.o.refreshToken) {
      let fresh: Token;
      try {
        fresh = await this.o.refreshToken();
      } catch {
        throw await ProblemError.fromResponse(res, correlationId);
      }
      this.o.onTokenRefreshed?.(fresh);
      try {
        res = await send(fresh);
      } catch (error) {
        throw ProblemError.network(error, correlationId);
      }
    }

    if (!res.ok) {
      throw await ProblemError.fromResponse(res, correlationId);
    }
    if (res.status === 204) {
      return undefined as T;
    }
    const text = await res.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  headers(token: Token | null | undefined, correlationId: string): Record<string, string> {
    const headers: Record<string, string> = {};
    for (const [name, value] of Object.entries(this.o.extraHeaders ?? {})) {
      if (!RESERVED.has(name.toLowerCase())) headers[name] = value;
    }
    headers["Accept"] = "application/json, application/problem+json";
    headers["Content-Type"] = "application/json; charset=utf-8";
    headers["X-Api-Key"] = this.o.apiKey;
    headers["X-Correlation-Id"] = correlationId;
    headers["X-Session-Id"] = this.o.sessionId;
    if (token?.access_token) {
      headers["Authorization"] = `Bearer ${token.access_token}`;
    }
    if (this.o.userIdentity) {
      headers["X-User-Identity"] = asciiJson(this.o.userIdentity);
    }
    return headers;
  }

  private correlationId(): string {
    const id = this.newId();
    const prefix = this.o.correlationPrefix?.replace(/[^A-Za-z0-9._:-]/g, "");
    return (prefix ? `${prefix}:${id}` : id).slice(0, 128);
  }
}
