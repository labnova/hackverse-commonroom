import Markdown from "react-markdown";
import type { ConversationMessage, Rating } from "../types";
import type { Strings } from "../i18n";

interface Props {
  message: ConversationMessage;
  t: Strings;
  onFeedback: (step: number, rating: Rating) => void;
}

export function Message({ message, t, onFeedback }: Props) {
  const isUser = message.role === "user";
  const sources = message.metadata?.source_documents ?? [];
  const rating = message.metadata?.feedback?.rating;

  return (
    <div className={`cr-msg ${isUser ? "cr-msg--user" : "cr-msg--assistant"}`}>
      <div className="cr-bubble" aria-label={isUser ? t.you : t.assistant}>
        {isUser ? (
          <p className="cr-plain">{message.content}</p>
        ) : (
          <div className="cr-md">
            <Markdown
              components={{
                a: ({ href, children }) => (
                  <a href={href} target="_blank" rel="noopener noreferrer">
                    {children}
                  </a>
                ),
              }}
            >
              {message.content}
            </Markdown>
          </div>
        )}
      </div>

      {message.role === "assistant" && (
        <div className="cr-meta">
          {sources.length > 0 && (
            <ul className="cr-sources" aria-label={t.sources}>
              {sources.map((doc) => (
                <li key={doc.id} className="cr-chip cr-chip--source" title={doc.quote ?? doc.name}>
                  <span aria-hidden="true">📄</span> {doc.name}
                </li>
              ))}
            </ul>
          )}
          <div className="cr-feedback">
            <button
              type="button"
              className={`cr-icon-btn ${rating === "up" ? "is-active" : ""}`}
              aria-pressed={rating === "up"}
              aria-label={t.helpful}
              title={t.helpful}
              onClick={() => onFeedback(message.conversation_step, "up")}
            >
              👍
            </button>
            <button
              type="button"
              className={`cr-icon-btn ${rating === "down" ? "is-active" : ""}`}
              aria-pressed={rating === "down"}
              aria-label={t.notHelpful}
              title={t.notHelpful}
              onClick={() => onFeedback(message.conversation_step, "down")}
            >
              👎
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
