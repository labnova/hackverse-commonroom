import { useAuth } from "react-oidc-context";
import { ChatHost } from "./ChatHost";
import { logoutUrl } from "./settings";

export function App() {
  const auth = useAuth();

  const logout = async () => {
    await auth.removeUser();
    window.location.assign(logoutUrl());
  };

  if (auth.isLoading) {
    return <p className="center">Caricamento…</p>;
  }

  if (auth.error) {
    return (
      <div className="center">
        <p>Errore di autenticazione: {auth.error.message}</p>
        <button type="button" onClick={() => void auth.signinRedirect()}>
          Riprova
        </button>
      </div>
    );
  }

  if (!auth.isAuthenticated) {
    return (
      <div className="center landing">
        <h1>Commonroom Labs</h1>
        <p>Demo host del widget Commonroom Chat.</p>
        <button type="button" className="primary" onClick={() => void auth.signinRedirect()}>
          Accedi con Cognito
        </button>
      </div>
    );
  }

  const email = typeof auth.user?.profile.email === "string" ? auth.user.profile.email : auth.user?.profile.sub;

  return (
    <div className="layout">
      <header className="topbar">
        <strong>Commonroom Labs · Intranet demo</strong>
        <span className="spacer" />
        <span className="who">{email}</span>
        <button type="button" onClick={() => void logout()}>
          Esci
        </button>
      </header>
      <main className="main">
        <ChatHost />
      </main>
    </div>
  );
}
