/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_OIDC_AUTHORITY: string;
  readonly VITE_OIDC_CLIENT_ID: string;
  readonly VITE_OIDC_REDIRECT_URI: string;
  readonly VITE_COGNITO_DOMAIN: string;
  readonly VITE_LOGOUT_URI: string;
  readonly VITE_API_BASE?: string;
  readonly VITE_API_KEY?: string;
  readonly VITE_VECTOR_STORE_ID?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
