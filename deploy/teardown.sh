#!/usr/bin/env bash
# Removes what deploy/setup.sh created, as recorded in deploy/.state.json.
# Usage: deploy/teardown.sh [--dry-run]
# Asks to type the PREFIX before deleting anything.
set -euo pipefail

DRY_RUN=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    -h|--help) sed -n '2,4p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$DEPLOY_DIR/.." && pwd)"
STATE="$DEPLOY_DIR/.state.json"
INFRA="$REPO/infra/Commonroom.Infra"
CDK_CONTEXT="$INFRA/cdk.context.json"
STACK=CommonroomAuthStack
CDK=(npx --yes aws-cdk@2)

info() { printf '   %s\n' "$*"; }
die()  { printf '\nERROR: %s\n' "$*" >&2; exit 1; }
run()  { printf '   + %s\n' "$*"; (( DRY_RUN )) && return 0; "$@"; }
npath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
json_get() {
  node -e 'const fs=require("fs");let s={};try{s=JSON.parse(fs.readFileSync(process.argv[1],"utf8"))}catch(e){}
const v=s[process.argv[2]];process.stdout.write(v===undefined||v===null?"":String(v))' "$(npath "$1")" "$2"
}

[[ -f "$STATE" ]] || die "deploy/.state.json not found: nothing to tear down."
PREFIX=$(json_get "$STATE" prefix)
RG=$(json_get "$STATE" resourceGroup)
FOUNDRY=$(json_get "$STATE" foundry)
AZ_LOCATION=$(json_get "$STATE" azLocation)
DOMAIN_PREFIX=$(json_get "$STATE" domainPrefix)
export AWS_PROFILE; AWS_PROFILE=$(json_get "$STATE" awsProfile)
export AWS_REGION; AWS_REGION=$(json_get "$STATE" awsRegion)
export CDK_DEFAULT_REGION="$AWS_REGION" AWS_DEFAULT_REGION="$AWS_REGION"
[[ -n "$PREFIX" ]] || die "deploy/.state.json has no prefix."

HAS_RG=$(json_get "$STATE" rgDone)
HAS_FOUNDRY=$(json_get "$STATE" foundryDone)
HAS_COGNITO=$(json_get "$STATE" cognitoDone)

echo "This deletes, for PREFIX=$PREFIX:"
[[ "$HAS_RG" == 1 ]] && info "Azure resource group $RG (Foundry, App Service, Static Web App) + purge of $FOUNDRY"
[[ "$HAS_COGNITO" == 1 ]] && info "AWS stack $STACK in $AWS_REGION (user pool, users, app clients)"
info "deploy/.state.json, and infra/Commonroom.Infra/cdk.context.json if it holds domainPrefix=$DOMAIN_PREFIX"

if (( DRY_RUN )); then
  info "dry-run: confirmation and login checks skipped"
else
  read -rp "Type the PREFIX to confirm: " answer
  [[ "$answer" == "$PREFIX" ]] || die "Not confirmed: nothing deleted."
  if [[ "$HAS_COGNITO" == 1 ]]; then
    aws sts get-caller-identity >/dev/null 2>&1 \
      || die "AWS session for profile '$AWS_PROFILE' missing or expired: run 'aws login --profile $AWS_PROFILE', then retry."
  fi
  if [[ "$HAS_RG" == 1 ]]; then
    az account get-access-token --query expiresOn -o tsv >/dev/null 2>&1 \
      || die "Azure session missing or expired: run 'az login', then retry."
  fi
fi

if [[ "$HAS_RG" == 1 ]]; then
  run az group delete -n "$RG" --yes
  [[ "$HAS_FOUNDRY" == 1 ]] && run az cognitiveservices account purge -n "$FOUNDRY" -g "$RG" -l "$AZ_LOCATION"
fi
if [[ "$HAS_COGNITO" == 1 ]]; then
  (cd "$INFRA" && run "${CDK[@]}" destroy "$STACK" --force)
fi

if [[ -f "$CDK_CONTEXT" && "$(json_get "$CDK_CONTEXT" domainPrefix)" == "$DOMAIN_PREFIX" ]]; then
  run rm -f "$CDK_CONTEXT"
fi
run rm -f "$STATE"

echo
info "Left in place (local only): apps/demo-host/.env.local, .env.production.local and the backend user-secrets."
info "To clear them: dotnet user-secrets clear --project backends/Commonroom.ReferenceApi"
(( DRY_RUN )) && info "DRY RUN: nothing was executed or deleted."
exit 0
