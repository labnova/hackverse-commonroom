import { useState } from "react";
import type { ChatSession, HistoryState } from "../core/session";
import type { Strings } from "../i18n";

interface Props {
  session: ChatSession;
  history: HistoryState;
  activeId: string | undefined;
  pending: boolean;
  open: boolean;
  t: Strings;
  onClose: () => void;
}

export function Sidebar({ session, history, activeId, pending, open, t, onClose }: Props) {
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [confirming, setConfirming] = useState<string | null>(null);

  const saveRename = async (id: string) => {
    if (await session.rename(id, draft)) setEditing(null);
  };

  return (
    <aside className={`cr-sidebar ${open ? "is-open" : ""}`} aria-label={t.history}>
      <div className="cr-sidebar__head">
        <span className="cr-sidebar__title">{t.history}</span>
        <button
          type="button"
          className="cr-btn cr-btn--primary cr-btn--sm"
          disabled={pending}
          onClick={() => {
            session.newConversation();
            onClose();
          }}
        >
          + {t.newConversation}
        </button>
      </div>

      <ul className="cr-history">
        {history.loaded && history.items.length === 0 && <li className="cr-history__empty">{t.noConversations}</li>}
        {history.items.map((c) => (
          <li key={c.id} className={`cr-history__item ${c.id === activeId ? "is-active" : ""}`}>
            {editing === c.id ? (
              <form
                className="cr-rename"
                onSubmit={(e) => {
                  e.preventDefault();
                  void saveRename(c.id);
                }}
              >
                <input
                  className="cr-rename__input"
                  value={draft}
                  maxLength={200}
                  autoFocus
                  aria-label={t.rename}
                  onChange={(e) => setDraft(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Escape") setEditing(null);
                  }}
                />
                <button type="submit" className="cr-icon-btn" title={t.save} aria-label={t.save}>
                  ✓
                </button>
                <button type="button" className="cr-icon-btn" title={t.cancel} aria-label={t.cancel} onClick={() => setEditing(null)}>
                  ✕
                </button>
              </form>
            ) : confirming === c.id ? (
              <div className="cr-confirm">
                <span>{t.confirmDelete}</span>
                <button
                  type="button"
                  className="cr-btn cr-btn--danger cr-btn--sm"
                  onClick={() => {
                    setConfirming(null);
                    void session.remove(c.id);
                  }}
                >
                  {t.delete}
                </button>
                <button type="button" className="cr-btn cr-btn--ghost cr-btn--sm" onClick={() => setConfirming(null)}>
                  {t.cancel}
                </button>
              </div>
            ) : (
              <>
                <button
                  type="button"
                  className="cr-history__open"
                  disabled={pending}
                  title={c.title}
                  onClick={() => {
                    void session.open(c.id);
                    onClose();
                  }}
                >
                  {c.title || t.untitled}
                </button>
                <button
                  type="button"
                  className="cr-icon-btn"
                  title={t.rename}
                  aria-label={t.rename}
                  onClick={() => {
                    setDraft(c.title);
                    setEditing(c.id);
                  }}
                >
                  ✎
                </button>
                <button type="button" className="cr-icon-btn" title={t.delete} aria-label={t.delete} onClick={() => setConfirming(c.id)}>
                  🗑
                </button>
              </>
            )}
          </li>
        ))}
      </ul>

      {history.items.length < history.total && (
        <button
          type="button"
          className="cr-btn cr-btn--ghost cr-btn--block"
          disabled={history.loading}
          onClick={() => void session.loadHistory(history.page + 1)}
        >
          {history.loading ? t.loading : t.loadMore}
        </button>
      )}
    </aside>
  );
}
