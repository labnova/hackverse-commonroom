import { useEffect, useRef } from "react";
import { useAuth } from "react-oidc-context";
import type { ConfigureFunction, TokenCallback } from "@commonroom/chat-widget";
import { toToken, widgetConfiguration } from "./settings";

declare global {
  interface Window {
    configureCommonroomChat?: ConfigureFunction;
  }
}

const CONFIGURE_FN = "configureCommonroomChat";

/** Mounts <commonroom-chat> imperatively and keeps feeding it the current Cognito tokens. */
export function ChatHost() {
  const auth = useAuth();
  const authRef = useRef(auth);
  authRef.current = auth;
  const callbackRef = useRef<TokenCallback | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    window.configureCommonroomChat = (cb) => {
      callbackRef.current = cb;
      const user = authRef.current.user;
      cb(user ? toToken(user) : undefined);
      return widgetConfiguration(user, async () => {
        const renewed = await authRef.current.signinSilent();
        if (!renewed) throw new Error("Silent renew returned no user");
        return toToken(renewed);
      });
    };

    const el = document.createElement("commonroom-chat");
    el.setAttribute("configure", CONFIGURE_FN);
    container.appendChild(el);

    return () => {
      el.remove();
      callbackRef.current = null;
      delete window.configureCommonroomChat;
    };
  }, []);

  // Pass every renewed token to the widget.
  useEffect(() => {
    if (auth.user) callbackRef.current?.(toToken(auth.user));
  }, [auth.user]);

  // A failed silent renew means the widget can no longer authenticate.
  useEffect(() => auth.events.addSilentRenewError(() => callbackRef.current?.(undefined)), [auth.events]);

  return <div ref={containerRef} className="chat-container" />;
}
