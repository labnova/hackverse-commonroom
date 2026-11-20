import type { Problem } from "../types";
import type { Strings } from "../i18n";

export function ErrorBanner({ problem, t, onDismiss }: { problem: Problem; t: Strings; onDismiss: () => void }) {
  return (
    <div className="cr-banner" role="alert">
      <div className="cr-banner__text">
        <strong>{problem.title}</strong>
        {problem.detail && <span className="cr-banner__detail">{problem.detail}</span>}
        {problem.correlation_id && (
          <span className="cr-banner__corr">
            {t.correlation}: <code>{problem.correlation_id}</code>
          </span>
        )}
      </div>
      <button type="button" className="cr-icon-btn" aria-label={t.dismiss} title={t.dismiss} onClick={onDismiss}>
        ✕
      </button>
    </div>
  );
}
