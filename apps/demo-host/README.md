# Commonroom Chat — demo host

React 19 + Vite app that signs in with Cognito (OIDC Authorization Code + PKCE via `react-oidc-context`)
and mounts the `<commonroom-chat>` web component imperatively, as the widget contract describes:

1. defines `window.configureCommonroomChat = (cb) => Configuration`;
2. `document.createElement("commonroom-chat")` with `configure="configureCommonroomChat"`;
3. passes the tokens to `cb` and again on every `auth.user` renewal; `refresh_token_callback` = `signinSilent()`.

## 1. Configure

```bash
cp apps/demo-host/.env.example apps/demo-host/.env.local   # gitignored
```

Fill it with the `CommonroomAuthStack` outputs (project-alpha client) and the `vector_store_id`
printed by `Commonroom.VectorSeed`.

## 2. Start the reference API (step 2 settings)

```powershell
$env:Chat__Engine = "Foundry"                 # or "Fake" (no Azure needed)
$env:VectorStores__Allowed__0 = "<vs_...>"
$env:Cognito__Region = "eu-central-1"
$env:Cognito__UserPoolId = "<UserPoolId>"
$env:Cognito__ClientId = "<ClientIdProjectAlpha>"
$env:ApiKeys__Allowed__0 = "dev-key"
$env:Cors__AllowedOrigins__0 = "http://localhost:5173"
dotnet run --project backends/Commonroom.ReferenceApi --urls http://localhost:5080
```

Foundry settings (`Foundry:ProjectEndpoint`, `Foundry:ModelDeployment`) come from user-secrets; `az login` is required.

## 3. Run the host

```bash
npm install          # at the repo root (npm workspaces)
npm run build -w @commonroom/chat-widget   # the host imports the built widget
npm run dev          # http://localhost:5173
```

Sign in as `test@commonroom.dev` (password in `%USERPROFILE%\.commonroom-dev.txt`).
Logout clears the local session and redirects to the Cognito `/logout` endpoint.
