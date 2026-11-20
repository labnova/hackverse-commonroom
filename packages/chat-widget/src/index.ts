import { defineCommonroomChat } from "./element";

export type {
  BackendProvider,
  ConfigureFunction,
  Configuration,
  Conversation,
  ConversationMessage,
  ConversationMetadata,
  Lang,
  Page,
  Problem,
  PromptSuggestion,
  SourceDocument,
  Token,
  TokenCallback,
  UserIdentifier,
} from "./types";
export type { ChatTransport } from "./core/transport";
export { CommonroomChatElement, TAG_NAME, defineCommonroomChat } from "./element";

// Importing the module registers <commonroom-chat>.
if (typeof window !== "undefined" && typeof customElements !== "undefined") {
  defineCommonroomChat();
}

declare global {
  interface HTMLElementTagNameMap {
    "commonroom-chat": import("./element").CommonroomChatElement;
  }
}
