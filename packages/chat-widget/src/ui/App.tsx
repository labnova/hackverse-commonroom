import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import type { ChatSession } from "../core/session";
import type { TokenStore } from "../core/tokens";
import { nameFromIdToken } from "../core/ids";
import type { Strings } from "../i18n";
import type { Configuration } from "../types";
import { Composer } from "./Composer";
import { ErrorBanner } from "./ErrorBanner";
import { Message } from "./Message";
import { Sidebar } from "./Sidebar";

interface Props {
  config: Configuration;
  tokens: TokenStore;
  session: ChatSession;
  t: Strings;
}

export function App({ config, tokens, session, t }: Props) {
  const token = useSyncExternalStore(tokens.subscribe, tokens.get);
  const state = useSyncExternalStore(session.subscribe, session.getState);
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const started = useRef(false);
  const listRef = useRef<HTMLDivElement>(null);
  const withHistory = config.ui_extensions?.historySidebar === true;

  useEffect(() => {
    if (!token || started.current) return;
    started.current = true;
    const initial = config.session?.conversationId;
    if (initial) void session.open(initial);
    if (withHistory) void session.loadHistory(1);
  }, [token, config, session, withHistory]);

  useEffect(() => {
    const el = listRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [state.messages.length, state.pending]);

  if (token === undefined) {
    return <div className="cr-status">{t.signingIn}</div>;
  }
  if (token === null) {
    return <div className="cr-status cr-status--error">{t.authFailed}</div>;
  }

  const name = nameFromIdToken(token.id_token) ?? config.display_identity;
  const suggestions = config.prompt_suggestions ?? [];
  const empty = state.messages.length === 0;

  return (
    <div className={`cr-root ${withHistory ? "cr-root--with-history" : ""}`}>
      {withHistory && (
        <>
          <Sidebar
            session={session}
            history={state.history}
            activeId={state.conversation?.id}
            pending={state.pending}
            open={sidebarOpen}
            t={t}
            onClose={() => setSidebarOpen(false)}
          />
          {sidebarOpen && <div className="cr-scrim" onClick={() => setSidebarOpen(false)} />}
        </>
      )}

      <section className="cr-panel">
        <header className="cr-header">
          {withHistory && (
            <button
              type="button"
              className="cr-icon-btn cr-header__menu"
              aria-label={t.toggleHistory}
              title={t.toggleHistory}
              aria-expanded={sidebarOpen}
              onClick={() => setSidebarOpen((v) => !v)}
            >
              ☰
            </button>
          )}
          <span className="cr-header__title">{state.conversation?.title || t.newConversation}</span>
          <button
            type="button"
            className="cr-icon-btn"
            title={t.newConversation}
            aria-label={t.newConversation}
            disabled={state.pending || empty}
            onClick={() => session.newConversation()}
          >
            ＋
          </button>
        </header>

        {state.error && <ErrorBanner problem={state.error} t={t} onDismiss={() => session.dismissError()} />}

        <div className="cr-messages" ref={listRef} aria-live="polite">
          {state.loadingConversation && <div className="cr-status">{t.loading}</div>}
          {empty && !state.loadingConversation && (
            <div className="cr-greeting">
              <p className="cr-greeting__title">{t.greeting(name)}</p>
              <p className="cr-greeting__hint">{t.greetingHint}</p>
            </div>
          )}
          {state.messages.map((m) => (
            <Message key={`${m.conversation_step}-${m.role}`} message={m} t={t} onFeedback={(step, r) => void session.feedback(step, r)} />
          ))}
          {state.pending && (
            <div className="cr-typing" role="status">
              <span className="cr-dot" />
              <span className="cr-dot" />
              <span className="cr-dot" />
              <span className="cr-typing__label">{t.typing}</span>
            </div>
          )}
        </div>

        {empty && suggestions.length > 0 && (
          <div className="cr-suggestions">
            {suggestions.map((s) => (
              <button
                key={s.title}
                type="button"
                className="cr-chip cr-chip--suggestion"
                title={s.prompt}
                disabled={state.pending}
                onClick={() => void session.send(s.prompt)}
              >
                {s.title}
              </button>
            ))}
          </div>
        )}

        <Composer t={t} pending={state.pending} supportsStop={session.supportsStop} onSend={(text) => session.send(text)} />
      </section>
    </div>
  );
}
