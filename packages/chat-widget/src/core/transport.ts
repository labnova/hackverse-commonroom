import type {
  BackendProvider,
  Conversation,
  ConversationMessage,
  ConversationMetadata,
  CreateConversationResult,
  Page,
  Rating,
} from "../types";
import type { HttpClient } from "./http";

/**
 * Everything the UI needs from a backend. "openapi" implements it over REST; "sse" / "ws"
 * (step 4) will implement the same interface, so the UI does not change.
 */
export interface ChatTransport {
  readonly provider: BackendProvider;
  /** Whether an in-flight answer can be interrupted (false for plain REST). */
  readonly supportsStop: boolean;
  createConversation(content: string, metadata: ConversationMetadata): Promise<CreateConversationResult>;
  /** Sends a user message on an existing conversation. Never sends metadata: the server keeps its own. */
  sendMessage(conversationId: string, content: string): Promise<ConversationMessage>;
  stop?(): void;
  listConversations(page: number, pageSize: number): Promise<Page<Conversation>>;
  getConversation(id: string): Promise<Conversation>;
  renameConversation(id: string, title: string): Promise<Conversation>;
  deleteConversation(id: string): Promise<void>;
  listMessages(id: string, page: number, pageSize: number): Promise<Page<ConversationMessage>>;
  setReadState(id: string, step: number): Promise<void>;
  sendFeedback(id: string, step: number, rating: Rating, comment?: string): Promise<void>;
}

const enc = encodeURIComponent;

export class OpenApiTransport implements ChatTransport {
  readonly provider = "openapi" as const;
  readonly supportsStop = false;
  private readonly http: HttpClient;

  constructor(http: HttpClient) {
    this.http = http;
  }

  createConversation(content: string, metadata: ConversationMetadata) {
    return this.http.post<CreateConversationResult>("/conversations", { content, metadata });
  }

  sendMessage(conversationId: string, content: string) {
    return this.http.post<ConversationMessage>(`/conversations/${enc(conversationId)}/messages`, { content });
  }

  listConversations(page: number, pageSize: number) {
    return this.http.get<Page<Conversation>>(`/conversations?page=${page}&page_size=${pageSize}`);
  }

  getConversation(id: string) {
    return this.http.get<Conversation>(`/conversations/${enc(id)}`);
  }

  renameConversation(id: string, title: string) {
    return this.http.patch<Conversation>(`/conversations/${enc(id)}`, { title });
  }

  deleteConversation(id: string) {
    return this.http.delete<void>(`/conversations/${enc(id)}`);
  }

  listMessages(id: string, page: number, pageSize: number) {
    return this.http.get<Page<ConversationMessage>>(`/conversations/${enc(id)}/messages?page=${page}&page_size=${pageSize}`);
  }

  setReadState(id: string, step: number) {
    return this.http.put<void>(`/conversations/${enc(id)}/read-state`, { step });
  }

  sendFeedback(id: string, step: number, rating: Rating, comment?: string) {
    return this.http.post<void>(`/conversations/${enc(id)}/messages/${step}/feedback`, comment ? { rating, comment } : { rating });
  }
}

export function createTransport(provider: BackendProvider | undefined, http: HttpClient): ChatTransport {
  switch (provider ?? "openapi") {
    case "openapi":
      return new OpenApiTransport(http);
    case "sse":
    case "ws":
      console.warn(`[commonroom-chat] backend_provider "${provider}" is not available yet: falling back to "openapi".`);
      return new OpenApiTransport(http);
    default:
      console.warn(`[commonroom-chat] unknown backend_provider "${String(provider)}": using "openapi".`);
      return new OpenApiTransport(http);
  }
}
