import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { WebStorageStateStore } from "oidc-client-ts";
import { AuthProvider, type AuthProviderProps } from "react-oidc-context";
// Registers the <commonroom-chat> custom element.
import "@commonroom/chat-widget";
import { App } from "./App";
import { oidc } from "./settings";
import "./styles.css";

const oidcConfig: AuthProviderProps = {
  authority: oidc.authority,
  client_id: oidc.clientId,
  redirect_uri: oidc.redirectUri,
  post_logout_redirect_uri: oidc.logoutUri,
  response_type: "code", // Authorization Code + PKCE (S256) by default in oidc-client-ts
  scope: "openid email profile",
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  onSigninCallback: () => {
    // Drop ?code=&state= and leave /callback.
    window.history.replaceState({}, document.title, "/");
  },
};

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AuthProvider {...oidcConfig}>
      <App />
    </AuthProvider>
  </StrictMode>,
);
