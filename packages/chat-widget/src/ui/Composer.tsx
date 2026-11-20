import { useState, type KeyboardEvent } from "react";
import type { Strings } from "../i18n";

interface Props {
  t: Strings;
  pending: boolean;
  supportsStop: boolean;
  onSend: (text: string) => Promise<boolean>;
  onStop?: () => void;
}

export function Composer({ t, pending, supportsStop, onSend, onStop }: Props) {
  const [text, setText] = useState("");

  const submit = async () => {
    const value = text.trim();
    if (!value || pending) return;
    setText("");
    const ok = await onSend(value);
    if (!ok) setText(value);
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      void submit();
    }
  };

  return (
    <form
      className="cr-composer"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <textarea
        className="cr-input"
        rows={1}
        value={text}
        placeholder={t.placeholder}
        aria-label={t.placeholder}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={onKeyDown}
      />
      {pending ? (
        <button
          type="button"
          className="cr-btn cr-btn--ghost"
          disabled={!supportsStop}
          title={supportsStop ? t.stop : t.stopUnavailable}
          onClick={onStop}
        >
          {t.stop}
        </button>
      ) : (
        <button type="submit" className="cr-btn cr-btn--primary" disabled={!text.trim()}>
          {t.send}
        </button>
      )}
    </form>
  );
}
