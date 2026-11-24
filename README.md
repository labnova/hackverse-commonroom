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

Azure deployment (Foundry, App Service, Static Web App): see [Deploy su Azure](#deploy-su-azure).

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

## Deploy su Azure

Low-cost POC layout: Foundry (Sweden Central) + App Service F1 Linux for the API + Static Web App Free for the
demo host, with Cognito (AWS) for login. Commands checked against `az` 2.88. Bash syntax; every value in `<...>`
is a placeholder.

```bash
RG=rg-commonroom-<suffix>
LOCATION=swedencentral                  # Foundry + App Service
SWA_LOCATION=westeurope                 # Static Web Apps is not offered in every region
FOUNDRY=foundry-commonroom-<suffix>     # also used as custom subdomain: globally unique
PROJECT=commonroom
DEPLOYMENT=chat-mini
PLAN=plan-commonroom-<suffix>
API=commonroom-api-<suffix>             # -> https://$API.azurewebsites.net
SWA=swa-commonroom-<suffix>
```

### a. Resource group

```bash
az group create -n $RG -l $LOCATION
```

### b. Foundry resource, project and model deployment

```bash
az cognitiveservices account create -n $FOUNDRY -g $RG -l $LOCATION \
  --kind AIServices --sku S0 --custom-domain $FOUNDRY --allow-project-management --assign-identity --yes

az cognitiveservices account project create -n $FOUNDRY -g $RG -l $LOCATION --project-name $PROJECT

az cognitiveservices account deployment create -n $FOUNDRY -g $RG --deployment-name $DEPLOYMENT \
  --model-name gpt-5.4-mini --model-version 2026-03-17 --model-format OpenAI \
  --sku-name GlobalStandard --sku-capacity 10

FOUNDRY_PROJECT_ENDPOINT="https://$FOUNDRY.services.ai.azure.com/api/projects/$PROJECT"
```

`--sku-capacity` is in thousands of tokens per minute; it is a rate limit, not a fixed cost.

### c. Vector store

Use `tools/Commonroom.VectorSeed` as described in [Seed a vector store](#seed-a-vector-store), with
`FOUNDRY_PROJECT_ENDPOINT` from step b. Keep the printed `vs_...` id for steps e and g.

### d. App Service plan (Linux F1) + .NET 10 web app with managed identity

```bash
az appservice plan create -n $PLAN -g $RG -l $LOCATION --is-linux --sku F1

az webapp create -n $API -g $RG -p $PLAN --runtime "DOTNETCORE:10.0" \
  --https-only true --min-tls-version 1.2 --startup-file "dotnet Commonroom.ReferenceApi.dll"
az webapp config set -n $API -g $RG --ftps-state Disabled

PRINCIPAL_ID=$(az webapp identity assign -n $API -g $RG --query principalId -o tsv)
FOUNDRY_ID=$(az cognitiveservices account show -n $FOUNDRY -g $RG --query id -o tsv)
az role assignment create --role "Foundry User" --scope "$FOUNDRY_ID" \
  --assignee-object-id "$PRINCIPAL_ID" --assignee-principal-type ServicePrincipal
```

The API calls Foundry only through `DefaultAzureCredential`, which in App Service resolves to the managed
identity: no Azure keys in the settings.

### e. App Service settings

Cognito values come from the outputs of [1. Deploy Cognito](#1-deploy-cognito) (client: `project-alpha`).
Generate a random API key without printing it:

```bash
API_KEY=$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')   # 32 random bytes, base64url
```

```bash
az webapp config appsettings set -n $API -g $RG --output none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Chat__Engine=Foundry \
  Foundry__ProjectEndpoint="$FOUNDRY_PROJECT_ENDPOINT" \
  Foundry__ModelDeployment=$DEPLOYMENT \
  Foundry__MaxOutputTokens=<max-output-tokens> \
  RateLimit__MessagesPerHour=<messages-per-hour> \
  VectorStores__Allowed__0=<vs_...> \
  Cognito__Region=<Region> \
  Cognito__UserPoolId=<UserPoolId> \
  Cognito__ClientId=<ClientIdProjectAlpha> \
  ApiKeys__Allowed__0="$API_KEY" \
  Cors__AllowedOrigins__0=https://<swa-hostname> \
  ConnectionStrings__Chat="Data Source=/home/data/commonroom.db"
```

`/home` is the persistent App Service storage; the API creates `/home/data` at startup.
`Cors__AllowedOrigins__0` is known only after step g: set it then (it can be re-run on its own).

### f. Publish and deploy the API (zip deploy)

```bash
dotnet publish backends/Commonroom.ReferenceApi -c Release -o out/api
(cd out/api && zip -qr ../api.zip .)
az webapp deploy -n $API -g $RG --src-path out/api.zip --type zip
```

On Windows PowerShell 5.1 do not use `Compress-Archive` (it writes `\` in zip paths and the Linux deployment
fails): use `tar.exe -a -c -f out\api.zip -C out\api .` instead.

### g. Static Web App (Free) + demo host

```bash
az staticwebapp create -n $SWA -g $RG -l $SWA_LOCATION --sku Free
SWA_HOST=$(az staticwebapp show -n $SWA -g $RG --query defaultHostname -o tsv)
```

`apps/demo-host/.env.production.local` (git-ignored by `.env.*`; check with `git check-ignore`):

```dotenv
VITE_OIDC_AUTHORITY=https://cognito-idp.<region>.amazonaws.com/<UserPoolId>
VITE_OIDC_CLIENT_ID=<ClientIdProjectAlpha>
VITE_OIDC_REDIRECT_URI=https://<swa-hostname>/callback
VITE_COGNITO_DOMAIN=https://<domain-prefix>.auth.<region>.amazoncognito.com
VITE_LOGOUT_URI=https://<swa-hostname>/
VITE_API_BASE=https://<api-name>.azurewebsites.net
VITE_API_KEY=<api-key>
VITE_VECTOR_STORE_ID=<vs_...>
```

The API key is embedded in the public bundle: it identifies the host, it is not a secret. Access is protected
by the Cognito token.

```bash
npm install && npm run build      # widget, then demo host -> apps/demo-host/dist
export SWA_CLI_DEPLOYMENT_TOKEN=$(az staticwebapp secrets list -n $SWA -g $RG --query properties.apiKey -o tsv)
(cd apps/demo-host && npx @azure/static-web-apps-cli deploy ./dist --env production)
unset SWA_CLI_DEPLOYMENT_TOKEN
```

`apps/demo-host/public/staticwebapp.config.json` provides the SPA fallback for `/callback`.

### h. Cognito: allow the Static Web App URLs

Re-deploy `CommonroomAuthStack` without `devAuth`, adding the Static Web App to `project-alpha`
(localhost stays from `cdk.json`):

```bash
cd infra/Commonroom.Infra
npx cdk diff CommonroomAuthStack \
  -c domainPrefix=commonroom-<unique-suffix> \
  -c callbackUrls:project-alpha=https://<swa-hostname>/callback \
  -c logoutUrls:project-alpha=https://<swa-hostname>/
npx cdk deploy CommonroomAuthStack \
  -c domainPrefix=commonroom-<unique-suffix> \
  -c callbackUrls:project-alpha=https://<swa-hostname>/callback \
  -c logoutUrls:project-alpha=https://<swa-hostname>/
```

Always pass the same `domainPrefix` used at the first deploy: without it the Hosted UI domain is replaced.
Check that `cdk diff` touches only what you expect. Without `devAuth` the clients only have
`ALLOW_REFRESH_TOKEN_AUTH`, so the CLI tokens of [step 3](#3-get-an-access-token-devauth-only) are not available.

### i. Verify

```bash
BASE=https://<api-name>.azurewebsites.net
curl -s -o /dev/null -w '%{http_code}\n' $BASE/health                        # 200 (first call: F1 cold start)
curl -s -H 'X-Api-Key: wrong' $BASE/conversations | jq -r .code             # invalid_api_key (401)
```

Then open `https://<swa-hostname>`, sign in with a user created in [step 2](#2-create-a-user-and-add-it-to-a-group)
and send a message.

### j. Costs and teardown

| Resource | Cost |
|---|---|
| App Service plan F1 | Free (CPU quota per day, no Always On: cold starts) |
| Static Web App Free | Free |
| Foundry resource S0 + project | No fixed fee |
| Model deployment (GlobalStandard) | Pay per token |
| Vector store / file search | Pay per use (storage and tool calls) |
| Cognito | Free tier for low monthly active users; AWS pricing beyond it |

```bash
az group delete -n $RG --yes
az cognitiveservices account purge -n $FOUNDRY -g $RG -l $LOCATION   # Foundry is soft-deleted: purge to reuse the name
(cd infra/Commonroom.Infra && npx cdk destroy CommonroomAuthStack)
```
