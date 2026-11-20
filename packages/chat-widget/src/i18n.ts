import type { Lang } from "./types";

export interface Strings {
  greeting: (name?: string) => string;
  greetingHint: string;
  placeholder: string;
  send: string;
  stop: string;
  stopUnavailable: string;
  typing: string;
  newConversation: string;
  history: string;
  toggleHistory: string;
  loadMore: string;
  noConversations: string;
  rename: string;
  save: string;
  cancel: string;
  delete: string;
  confirmDelete: string;
  sources: string;
  helpful: string;
  notHelpful: string;
  correlation: string;
  dismiss: string;
  signingIn: string;
  authFailed: string;
  loading: string;
  untitled: string;
  you: string;
  assistant: string;
}

const it: Strings = {
  greeting: (name) => (name ? `Ciao ${name}!` : "Ciao!"),
  greetingHint: "Come posso aiutarti? Rispondo in base ai documenti del progetto.",
  placeholder: "Scrivi un messaggio…",
  send: "Invia",
  stop: "Interrompi",
  stopUnavailable: "Interruzione non disponibile con il trasporto REST",
  typing: "Sta scrivendo…",
  newConversation: "Nuova conversazione",
  history: "Conversazioni",
  toggleHistory: "Mostra o nascondi le conversazioni",
  loadMore: "Carica altre",
  noConversations: "Nessuna conversazione",
  rename: "Rinomina",
  save: "Salva",
  cancel: "Annulla",
  delete: "Elimina",
  confirmDelete: "Eliminare?",
  sources: "Fonti",
  helpful: "Risposta utile",
  notHelpful: "Risposta non utile",
  correlation: "ID correlazione",
  dismiss: "Chiudi",
  signingIn: "Accesso in corso…",
  authFailed: "Autenticazione non riuscita. Effettua di nuovo l'accesso.",
  loading: "Caricamento…",
  untitled: "Senza titolo",
  you: "Tu",
  assistant: "Assistente",
};

const en: Strings = {
  greeting: (name) => (name ? `Hi ${name}!` : "Hi!"),
  greetingHint: "How can I help? I answer from the project's documents.",
  placeholder: "Type a message…",
  send: "Send",
  stop: "Stop",
  stopUnavailable: "Stop is not available with the REST transport",
  typing: "Typing…",
  newConversation: "New conversation",
  history: "Conversations",
  toggleHistory: "Show or hide conversations",
  loadMore: "Load more",
  noConversations: "No conversations",
  rename: "Rename",
  save: "Save",
  cancel: "Cancel",
  delete: "Delete",
  confirmDelete: "Delete?",
  sources: "Sources",
  helpful: "Helpful answer",
  notHelpful: "Not helpful",
  correlation: "Correlation ID",
  dismiss: "Dismiss",
  signingIn: "Signing in…",
  authFailed: "Authentication failed. Please sign in again.",
  loading: "Loading…",
  untitled: "Untitled",
  you: "You",
  assistant: "Assistant",
};

export function strings(lang: Lang | undefined): Strings {
  return lang === "en" ? en : it;
}
