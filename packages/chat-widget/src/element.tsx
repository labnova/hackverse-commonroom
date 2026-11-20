import { StrictMode } from "react";
import { createRoot, type Root } from "react-dom/client";
import css from "./styles.css?inline";
import { resolveConfiguration } from "./core/config";
import { HttpClient } from "./core/http";
import { uuid } from "./core/ids";
import { ChatSession } from "./core/session";
import { TokenStore } from "./core/tokens";
import { createTransport } from "./core/transport";
import { strings } from "./i18n";
import { App } from "./ui/App";

export const TAG_NAME = "commonroom-chat";

/**
 * <commonroom-chat configure="fnName">: calls window[fnName](tokenCallback) synchronously on connect,
 * then renders the chat inside an open Shadow DOM with its own styles.
 */
export class CommonroomChatElement extends HTMLElement {
  private root: Root | null = null;

  connectedCallback(): void {
    if (this.root) return;

    const shadow = this.shadowRoot ?? this.attachShadow({ mode: "open" });
    shadow.replaceChildren();
    const style = document.createElement("style");
    style.textContent = css;
    const mount = document.createElement("div");
    mount.className = "cr-host";
    shadow.append(style, mount);
    this.root = createRoot(mount);

    try {
      const tokens = new TokenStore();
      const config = resolveConfiguration(this.getAttribute("configure"), tokens);
      const sessionId = config.session?.sessionId || uuid();
      const http = new HttpClient({
        basePath: config.base_path,
        apiKey: config.api_key,
        sessionId,
        getToken: tokens.get,
        refreshToken: config.refresh_token_callback,
        onTokenRefreshed: (token) => tokens.set(token),
        userIdentity: config.white_label_custom_config?.user_identifier,
        extraHeaders: config.extra_headers,
        correlationPrefix: config.session?.correlationId,
      });
      const session = new ChatSession(createTransport(config.backend_provider, http), {
        sessionId,
        metadata: config.white_label_custom_config?.metadata,
      });
      const t = strings(config.lang);

      this.setAttribute("lang", config.lang ?? "it");
      this.root.render(
        <StrictMode>
          <App config={config} tokens={tokens} session={session} t={t} />
        </StrictMode>,
      );
    } catch (error) {
      console.error(error);
      const message = error instanceof Error ? error.message : String(error);
      this.root.render(<div className="cr-status cr-status--error">{message}</div>);
    }
  }

  disconnectedCallback(): void {
    const root = this.root;
    this.root = null;
    root?.unmount();
  }
}

export function defineCommonroomChat(): void {
  if (!customElements.get(TAG_NAME)) {
    customElements.define(TAG_NAME, CommonroomChatElement);
  }
}
