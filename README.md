# Commonroom Chat — Cognito, contract, reference backend

| Path | Content |
|---|---|
| `contracts/commonroom-chat.openapi.yaml` | OpenAPI 3.1 contract (`/conversations`, `/me`, `/health`) |
| `infra/Commonroom.Infra` | AWS CDK (C#) — `CommonroomAuthStack` |
| `backends/Commonroom.ReferenceApi` | .NET 10 minimal API: Cognito auth + `/conversations` (EF Core SQLite, Fake/Foundry engine) |
| `tools/Commonroom.VectorSeed` | Console tool: creates a Foundry vector store from a docs folder |
| `tests/Commonroom.ReferenceApi.Tests` | xUnit integration tests (WebApplicationFactory, Fake engine, local test JWT) |
| `samples/docs` | Sample documents (fictional Commonroom Labs) |
| `packages/chat-widget`, `apps/demo-host` | reserved (later steps) |

No real IDs or secrets are committed. Put local values in user-secrets or environment variables.

## 1. Deploy Cognito

```bash
cd infra/Commonroom.Infra
npx cdk bootstrap                       # once per account/region
npx cdk deploy \
  -c projects='["demo-host","project-alpha"]' \
  -c devAuth=true \
  -c domainPrefix=commonroom-<unique-suffix> \
  --outputs-file cdk.out/outputs.json
```

- `projects`: one public app client per project (no secret, Authorization Code + PKCE,
  scopes `openid email profile`, refresh token, callback `http://localhost:5173/callback`).
  Also accepted as a comma list: `-c projects=demo-host,project-alpha`.
- `devAuth=true` adds `ALLOW_USER_PASSWORD_AUTH` (CLI tokens below). **Omit it outside dev.**
- Outputs: `UserPoolId`, `Region`, `HostedUiDomain`, `ClientIdDemoHost`, `ClientIdProjectAlpha`, …

```bash
export REGION=<Region>
export POOL_ID=<UserPoolId>
export DEMO_HOST_CLIENT_ID=<ClientIdDemoHost>
export ALPHA_CLIENT_ID=<ClientIdProjectAlpha>
```

## 2. Create a user and add it to a group

```bash
aws cognito-idp admin-create-user --region $REGION --user-pool-id $POOL_ID \
  --username alice@example.com \
  --user-attributes Name=email,Value=alice@example.com Name=email_verified,Value=true \
  --message-action SUPPRESS

aws cognito-idp admin-set-user-password --region $REGION --user-pool-id $POOL_ID \
  --username alice@example.com --password '<StrongPassword123>' --permanent

aws cognito-idp admin-add-user-to-group --region $REGION --user-pool-id $POOL_ID \
  --username alice@example.com --group-name members      # or curators
```

## 3. Get an access token (devAuth only)

```bash
ALPHA_TOKEN=$(aws cognito-idp initiate-auth --region $REGION \
  --client-id $ALPHA_CLIENT_ID --auth-flow USER_PASSWORD_AUTH \
  --auth-parameters USERNAME=alice@example.com,PASSWORD='<StrongPassword123>' \
  --query 'AuthenticationResult.AccessToken' --output text)

DEMO_TOKEN=$(aws cognito-idp initiate-auth --region $REGION \
  --client-id $DEMO_HOST_CLIENT_ID --auth-flow USER_PASSWORD_AUTH \
  --auth-parameters USERNAME=alice@example.com,PASSWORD='<StrongPassword123>' \
  --query 'AuthenticationResult.AccessToken' --output text)
```

Browsers use the Hosted UI with PKCE instead:
`<HostedUiDomain>/oauth2/authorize?response_type=code&client_id=<id>&redirect_uri=http://localhost:5173/callback&scope=openid+email+profile&code_challenge=<S256>&code_challenge_method=S256`.

Note: Cognito access tokens do not carry `email`; `/me` returns `email: null` unless the token has it.

## 4. Run the reference backend (configured as project-alpha)

```bash
cd backends/Commonroom.ReferenceApi
dotnet user-secrets init
dotnet user-secrets set "Cognito:Region" "$REGION"
dotnet user-secrets set "Cognito:UserPoolId" "$POOL_ID"
dotnet user-secrets set "Cognito:ClientId" "$ALPHA_CLIENT_ID"
dotnet user-secrets set "ApiKeys:Allowed:0" "dev-key-change-me"
dotnet run                                   # http://localhost:5080
```

Equivalent env vars: `Cognito__Region`, `Cognito__UserPoolId`, `Cognito__ClientId`, `ApiKeys__Allowed__0`, `Cors__AllowedOrigins__0`.

## 5. Tests (curl)

```bash
API=http://localhost:5080
KEY=dev-key-change-me

# health (no auth)
curl -i $API/health

# 1) no token -> 401 application/problem+json, code=unauthorized
curl -i $API/me

# 2) token but no X-Api-Key -> 401 code=invalid_api_key
curl -i $API/me -H "Authorization: Bearer $ALPHA_TOKEN"

# 3) token issued for demo-host on a backend configured for project-alpha -> 401 unauthorized
curl -i $API/me -H "Authorization: Bearer $DEMO_TOKEN" -H "X-Api-Key: $KEY"

# 4) valid call
curl -i $API/me -H "Authorization: Bearer $ALPHA_TOKEN" -H "X-Api-Key: $KEY"

# 5) forged X-User-Identity -> 200, identity from token, header_identity_matches=false (+ warning in logs)
curl -i $API/me -H "Authorization: Bearer $ALPHA_TOKEN" -H "X-Api-Key: $KEY" \
  -H 'X-User-Identity: {"user_id":"someone-else","user_name":"mallory","user_email":"m@example.com","user_role":"curators"}'

# 6) list conversations (paginated) + X-Correlation-Id echoed
curl -i "$API/conversations?page=1&page_size=20" -H "Authorization: Bearer $ALPHA_TOKEN" \
  -H "X-Api-Key: $KEY" -H "X-Correlation-Id: test-123"
```

## Build & lint

```bash
dotnet build Commonroom.sln
npx @redocly/cli lint contracts/commonroom-chat.openapi.yaml
```

## Step 2 — `/conversations` implementation

Persistence: EF Core SQLite (`commonroom-ref.db`, migrations applied at startup). Every query is filtered by the
token `sub`; another user's conversation returns **404**.

| Setting | Meaning |
|---|---|
| `Chat:Engine` | `Fake` (default, deterministic `echo: <text>`) or `Foundry` |
| `Foundry:ProjectEndpoint` / `Foundry:ModelDeployment` | Azure AI Foundry project endpoint and model deployment (empty in repo) |
| `VectorStores:Allowed` | Allowed `vector_store_id` values; others → 403 `vector_store_forbidden` |
| `ConnectionStrings:Chat` | SQLite connection string |

Foundry uses **only** `DefaultAzureCredential` (run `az login`); no Azure API keys.

### Seed a vector store

```bash
az login
export FOUNDRY_PROJECT_ENDPOINT="https://<resource>.services.ai.azure.com/api/projects/<project>"
dotnet run --project tools/Commonroom.VectorSeed -- samples/docs commonroom-handbook
# -> vector_store_id=vs_...   files ok=2 failed=0
```

### Run with Foundry

```bash
cd backends/Commonroom.ReferenceApi
export Chat__Engine=Foundry
export Foundry__ProjectEndpoint="$FOUNDRY_PROJECT_ENDPOINT"
export Foundry__ModelDeployment="<deployment-name>"
export VectorStores__Allowed__0="vs_..."
dotnet run
```

### Try it

```bash
CID=$(curl -s $API/conversations -H "Authorization: Bearer $ALPHA_TOKEN" -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{"content":"Quanti giorni di ferie ho?","metadata":{"vector_store_id":"vs_..."}}' | jq -r .conversation.id)

curl -s $API/conversations/$CID/messages -H "Authorization: Bearer $ALPHA_TOKEN" -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" -d '{"content":"E quanti ne posso riportare?"}'

curl -s -X POST $API/conversations/$CID/messages/4/feedback -H "Authorization: Bearer $ALPHA_TOKEN" -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" -d '{"rating":"up"}'
```

### Tests

```bash
dotnet test Commonroom.sln
```

## Step 3 — widget + demo host

| Path | Content |
|---|---|
| `packages/chat-widget` | `<commonroom-chat>` web component (React 19, Shadow DOM, Vite library mode → `dist/commonroom-chat.js`) |
| `apps/demo-host` | Vite + React host: Cognito OIDC (react-oidc-context), mounts the widget imperatively |

```bash
npm install                 # npm workspaces: packages/*, apps/*
npm run build               # widget (tsc + vite + d.ts + gzip size check) then host
npm test                    # widget unit tests (vitest)
npm run dev                 # host on http://localhost:5173 (build the widget first)
```

Widget contract: `<commonroom-chat configure="fnName">` calls `window[fnName](tokenCallback)` synchronously and
expects a `Configuration` (`api_key` required). Transports implement `ChatTransport`; `backend_provider`
`"sse"`/`"ws"` currently warn and fall back to `"openapi"`. See `apps/demo-host/README.md` for the full run.
