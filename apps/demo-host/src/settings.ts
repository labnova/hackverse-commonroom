import type { Configuration, Token } from "@commonroom/chat-widget";
import type { User } from "oidc-client-ts";

const env = import.meta.env;

function required(name: keyof ImportMetaEnv): string {
  const value = env[name];
  if (!value) throw new Error(`Missing ${name}: copy apps/demo-host/.env.example to .env.local and fill it in.`);
  return value;
}

export const oidc = {
  authority: required("VITE_OIDC_AUTHORITY"),
  clientId: required("VITE_OIDC_CLIENT_ID"),
  redirectUri: required("VITE_OIDC_REDIRECT_URI"),
  cognitoDomain: required("VITE_COGNITO_DOMAIN").replace(/\/+$/, ""),
  logoutUri: required("VITE_LOGOUT_URI"),
};

export function logoutUrl(): string {
  const q = new URLSearchParams({ client_id: oidc.clientId, logout_uri: oidc.logoutUri });
  return `${oidc.cognitoDomain}/logout?${q.toString()}`;
}

export function toToken(user: User): Token {
  return {
    id_token: user.id_token ?? "",
    access_token: user.access_token,
    refresh_token: user.refresh_token ?? "",
    expires_in: user.expires_in ?? 0,
    token_type: user.token_type || "Bearer",
  };
}

/** Widget configuration returned by window.configureCommonroomChat. */
export function widgetConfiguration(user: User | null | undefined, refresh: () => Promise<Token>): Configuration {
  const profile = user?.profile;
  const groups = profile?.["cognito:groups"];
  const role = Array.isArray(groups) && typeof groups[0] === "string" ? groups[0] : "members";
  const email = typeof profile?.email === "string" ? profile.email : "";

  return {
    api_key: env.VITE_API_KEY || "dev-key",
    base_path: env.VITE_API_BASE || "http://localhost:5080",
    backend_provider: "openapi",
    lang: "it",
    white_label_custom_config: {
      user_identifier: profile
        ? { user_id: profile.sub, user_name: profile.name ?? email, user_email: email, user_role: role }
        : undefined,
      metadata: { vector_store_id: env.VITE_VECTOR_STORE_ID || null },
    },
    ui_extensions: { historySidebar: true },
    prompt_suggestions: [
      { title: "Giorni di ferie", prompt: "Quanti giorni di ferie ho all'anno in Commonroom Labs?" },
      { title: "Richiedere le ferie", prompt: "Come si richiedono le ferie e con quanto preavviso?" },
      { title: "Primo giorno", prompt: "Cosa succede il primo giorno di onboarding?" },
    ],
    refresh_token_callback: refresh,
    display_identity: email || undefined,
  };
}
