import type { Conversation, ConversationMessage, ConversationMetadata, Problem, Rating } from "../types";
import { toProblem } from "./http";
import type { ChatTransport } from "./transport";

export interface HistoryState {
  items: Conversation[];
  page: number;
  total: number;
  loading: boolean;
  loaded: boolean;
}

export interface ChatState {
  conversation: Conversation | null;
  messages: ConversationMessage[];
  loadingConversation: boolean;
  /** An answer is being produced ("sta scrivendo"). */
  pending: boolean;
  error: Problem | null;
  history: HistoryState;
}

export interface ChatSessionOptions {
  sessionId: string;
  /** Metadata from the host configuration: used ONLY when a new conversation is created. */
  metadata?: ConversationMetadata;
  historyPageSize?: number;
}

const MESSAGE_PAGE = 100;
const MAX_MESSAGE_PAGES = 10;

/** Framework-agnostic chat state machine on top of a ChatTransport (React subscribes via useSyncExternalStore). */
export class ChatSession {
  private state: ChatState = {
    conversation: null,
    messages: [],
    loadingConversation: false,
    pending: false,
    error: null,
    history: { items: [], page: 0, total: 0, loading: false, loaded: false },
  };
  private readonly listeners = new Set<() => void>();
  private readonly transport: ChatTransport;
  private readonly options: ChatSessionOptions;

  constructor(transport: ChatTransport, options: ChatSessionOptions) {
    this.transport = transport;
    this.options = options;
  }

  get supportsStop(): boolean {
    return this.transport.supportsStop;
  }

  getState = (): ChatState => this.state;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Opens an existing conversation: its metadata always comes from the server. */
  async open(id: string): Promise<void> {
    this.set({ loadingConversation: true, error: null });
    try {
      const conversation = await this.transport.getConversation(id);
      const messages: ConversationMessage[] = [];
      for (let page = 1; page <= MAX_MESSAGE_PAGES; page++) {
        const result = await this.transport.listMessages(id, page, MESSAGE_PAGE);
        messages.push(...result.items);
        if (messages.length >= result.total || result.items.length === 0) break;
      }
      this.set({ conversation, messages, loadingConversation: false, pending: false });
      void this.markRead();
    } catch (error) {
      this.set({ loadingConversation: false, error: toProblem(error) });
    }
  }

  newConversation(): void {
    if (this.state.pending) return;
    this.set({ conversation: null, messages: [], error: null });
  }

  /** Sends a user message. Resolves false on failure so the composer can restore the text. */
  async send(text: string): Promise<boolean> {
    const content = text.trim();
    if (!content || this.state.pending) return false;

    const current = this.state.conversation;
    const lastStep = this.state.messages.reduce((max, m) => Math.max(max, m.conversation_step), 0);
    const optimistic: ConversationMessage = {
      session_id: this.options.sessionId,
      conversation_id: current?.id ?? "",
      correlation_id: "",
      timestamp: new Date().toISOString(),
      conversation_step: lastStep + 1,
      role: "user",
      content,
    };
    this.set({ messages: [...this.state.messages, optimistic], pending: true, error: null });

    try {
      if (!current) {
        // New conversation: the only time configuration metadata is sent.
        const result = await this.transport.createConversation(content, { ...(this.options.metadata ?? {}) });
        this.set({ conversation: result.conversation, messages: result.messages, pending: false });
        this.upsertHistory(result.conversation);
      } else {
        // Existing conversation: no metadata, the server uses the stored one.
        const reply = await this.transport.sendMessage(current.id, content);
        const user: ConversationMessage = {
          ...optimistic,
          conversation_id: current.id,
          conversation_step: reply.conversation_step - 1,
          correlation_id: reply.correlation_id,
        };
        const others = this.state.messages.filter((m) => m !== optimistic);
        const conversation: Conversation = { ...current, updated_at: reply.timestamp };
        this.set({ conversation, messages: [...others, user, reply], pending: false });
        this.upsertHistory(conversation);
      }
      void this.markRead();
      return true;
    } catch (error) {
      this.set({
        messages: this.state.messages.filter((m) => m !== optimistic),
        pending: false,
        error: toProblem(error),
      });
      return false;
    }
  }

  async feedback(step: number, rating: Rating): Promise<void> {
    const conversation = this.state.conversation;
    if (!conversation) return;
    try {
      await this.transport.sendFeedback(conversation.id, step, rating);
      this.set({
        messages: this.state.messages.map((m) =>
          m.conversation_step === step
            ? { ...m, feedbackNeeded: false, metadata: { ...m.metadata, feedback: { rating, created_at: new Date().toISOString() } } }
            : m,
        ),
      });
    } catch (error) {
      this.set({ error: toProblem(error) });
    }
  }

  async loadHistory(page = 1): Promise<void> {
    const size = this.options.historyPageSize ?? 20;
    this.set({ history: { ...this.state.history, loading: true } });
    try {
      const result = await this.transport.listConversations(page, size);
      const items = page === 1 ? result.items : [...this.state.history.items, ...result.items.filter((c) => !this.state.history.items.some((x) => x.id === c.id))];
      this.set({ history: { items, page: result.page, total: result.total, loading: false, loaded: true } });
    } catch (error) {
      this.set({ history: { ...this.state.history, loading: false }, error: toProblem(error) });
    }
  }

  async rename(id: string, title: string): Promise<boolean> {
    const clean = title.trim();
    if (!clean) return false;
    try {
      const updated = await this.transport.renameConversation(id, clean);
      this.upsertHistory(updated, false);
      if (this.state.conversation?.id === id) this.set({ conversation: updated });
      return true;
    } catch (error) {
      this.set({ error: toProblem(error) });
      return false;
    }
  }

  async remove(id: string): Promise<void> {
    try {
      await this.transport.deleteConversation(id);
      const items = this.state.history.items.filter((c) => c.id !== id);
      this.set({ history: { ...this.state.history, items, total: Math.max(0, this.state.history.total - 1) } });
      if (this.state.conversation?.id === id) this.set({ conversation: null, messages: [] });
    } catch (error) {
      this.set({ error: toProblem(error) });
    }
  }

  dismissError(): void {
    this.set({ error: null });
  }

  private async markRead(): Promise<void> {
    const conversation = this.state.conversation;
    const last = this.state.messages.reduce((max, m) => Math.max(max, m.conversation_step), 0);
    if (!conversation || last <= (conversation.last_read_step ?? 0)) return;
    try {
      await this.transport.setReadState(conversation.id, last);
      if (this.state.conversation?.id === conversation.id) {
        this.set({ conversation: { ...this.state.conversation, last_read_step: last } });
      }
    } catch {
      // read-state is best effort
    }
  }

  private upsertHistory(conversation: Conversation, moveToTop = true): void {
    const items = this.state.history.items;
    const exists = items.some((c) => c.id === conversation.id);
    const next = exists
      ? moveToTop
        ? [conversation, ...items.filter((c) => c.id !== conversation.id)]
        : items.map((c) => (c.id === conversation.id ? conversation : c))
      : [conversation, ...items];
    this.set({ history: { ...this.state.history, items: next, total: this.state.history.total + (exists ? 0 : 1) } });
  }

  private set(patch: Partial<ChatState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }
}
