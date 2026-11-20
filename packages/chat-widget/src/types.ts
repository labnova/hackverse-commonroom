// ---- Host contract -------------------------------------------------------

/** Tokens handed over by the host after its own OIDC login (Cognito). */
export interface Token {
  id_token: string;
  access_token: string;
  refresh_token: string;
  expires_in: number;
  token_type: string;
}

/** Called by the host with fresh tokens (any number of times); `undefined` means authentication failed. */
export type TokenCallback = (token: Token | undefined) => void;

export type BackendProvider = "openapi" | "sse" | "ws";
export type Lang = "it" | "en";

export interface UserIdentifier {
  user_id: string;
  user_name: string;
  user_email: string;
  user_role: string;
}

export interface ConversationMetadata {
  vector_store_id?: string | null;
  [key: string]: unknown;
}

export interface PromptSuggestion {
  title: string;
  prompt: string;
}

export interface Configuration {
  /** Project API key sent as X-Api-Key (required). */
  api_key: string;
  /** Backend base URL, e.g. http://localhost:5080. Empty = same origin. */
  base_path?: string;
  /** Transport. "sse" and "ws" are reserved: they currently fall back to "openapi". */
  backend_provider?: BackendProvider;
  lang?: Lang;
  white_label_custom_config?: {
    /** Sent as X-User-Identity. Informational only: the backend trusts the token. */
    user_identifier?: UserIdentifier;
    /** Used only when a new conversation is created. */
    metadata?: ConversationMetadata;
  };
  session?: {
    sessionId?: string;
    /** Opens this existing conversation at startup. */
    conversationId?: string;
    /** Optional prefix for the per-request X-Correlation-Id. */
    correlationId?: string;
  };
  ui_extensions?: {
    historySidebar?: boolean;
  };
  prompt_suggestions?: PromptSuggestion[];
  /** Called once on a 401 to obtain fresh tokens before retrying the request. */
  refresh_token_callback?: () => Promise<Token>;
  /** Additional request headers (must be allowed by the backend CORS policy). */
  extra_headers?: Record<string, string>;
  /** Name shown in the greeting when the id_token has no name/given_name/email. */
  display_identity?: string;
}

/** window[configure] signature: called synchronously when the element connects. */
export type ConfigureFunction = (cb: TokenCallback) => Configuration;

// ---- REST contract (contracts/commonroom-chat.openapi.yaml) ------------------

export type MessageRole = "user" | "assistant" | "tool" | "system" | "extra" | "welcome";
export type Rating = "up" | "down";

export interface SourceDocumentMetadata {
  document: string;
  key: string;
  paragraph_id: number;
  temporary_url?: string | null;
  h1?: string | null;
  h2?: string | null;
  h3?: string | null;
}

export interface SourceDocument {
  page_content: string;
  metadata: SourceDocumentMetadata;
}

export interface Feedback {
  rating: Rating;
  comment?: string | null;
  created_at?: string;
}

export interface TokenUsage {
  completion_input_tokens: number;
  completion_output_tokens: number;
  embedding_tokens: number;
}

export interface ConversationStepMetadata {
  source_documents?: SourceDocument[];
  suggested_prompts?: PromptSuggestion[];
  thinking?: string;
  tool_calls?: unknown[];
  tokens?: TokenUsage;
  feedback?: Feedback;
}

export interface ConversationMessage {
  session_id: string;
  conversation_id: string;
  correlation_id: string;
  timestamp: string;
  conversation_step: number;
  role: MessageRole;
  content: string;
  feedbackNeeded?: boolean;
  metadata?: ConversationStepMetadata;
}

export interface Conversation {
  id: string;
  title: string;
  created_at: string;
  updated_at: string;
  last_read_step?: number;
  metadata: ConversationMetadata;
}

export interface Page<T> {
  items: T[];
  page: number;
  page_size: number;
  total: number;
}

export interface CreateConversationResult {
  conversation: Conversation;
  messages: ConversationMessage[];
}

/** RFC 9457 problem details as returned by Commonroom backends. */
export interface Problem {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  code?: string;
  correlation_id?: string;
}
