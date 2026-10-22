#!/usr/bin/env bash
# Create the shared App Service plan, API/web apps, and shared monitoring, then apply settings.
# Publishing the bits is done by azure/webapps-deploy after this script.
set -euo pipefail

RG="${AZURE_RESOURCE_GROUP:-rg-grabbike-dev}"
PLAN="${AZURE_APP_SERVICE_PLAN:-plan-grabbike}"
API_APP="${AZURE_API_APP:-app-grabbike-api}"
WEB_APP="${AZURE_WEB_APP:-app-grabbike-web}"
LOCATION="${AZURE_LOCATION:-southeastasia}"

pick_runtime() {
  local pattern="$1"
  local runtimes line
  # Current az returns "NAME|VERSION<tab>end-of-support<tab>...". --runtime accepts only the first field.
  runtimes="$(az webapp list-runtimes --os linux -o tsv | tr -d '\r')"
  line="$(printf '%s\n' "$runtimes" | grep -E -i "$pattern" | head -n 1 || true)"
  printf '%s' "$line" | awk -F '\t' '{ print $1 }' | tr ':' '|'
}

api_runtime="$(pick_runtime '^DOTNETCORE[:|]10' || true)"
if [ -z "$api_runtime" ]; then
  api_runtime="$(pick_runtime '^DOTNETCORE[:|]8' || true)"
fi
web_runtime="$(pick_runtime '^NODE[:|]22([.-]|[[:space:]]|$)' || true)"
if [ -z "$web_runtime" ]; then
  echo "Linux Node.js 22 is required by the web telemetry SDK but is unavailable in this subscription." >&2
  exit 1
fi

if [ -z "$api_runtime" ]; then
  echo "Could not find a Linux .NET runtime in this subscription." >&2
  exit 1
fi

echo "API runtime: $api_runtime"
echo "Web runtime: $web_runtime"

if ! az appservice plan show --resource-group "$RG" --name "$PLAN" >/dev/null 2>&1; then
  az appservice plan create \
    --resource-group "$RG" \
    --name "$PLAN" \
    --sku B1 \
    --is-linux \
    --location "$LOCATION" --output none
fi

if ! az webapp show --resource-group "$RG" --name "$API_APP" >/dev/null 2>&1; then
  az webapp create \
    --resource-group "$RG" \
    --plan "$PLAN" \
    --name "$API_APP" \
    --runtime "$api_runtime" --output none
fi

if ! az webapp show --resource-group "$RG" --name "$WEB_APP" >/dev/null 2>&1; then
  az webapp create \
    --resource-group "$RG" \
    --plan "$PLAN" \
    --name "$WEB_APP" \
    --runtime "$web_runtime" --output none
fi

az webapp config set \
  --resource-group "$RG" \
  --name "$API_APP" \
  --always-on true \
  --web-sockets-enabled true \
  --startup-file "chmod +x ./GrabBike.Api && ./GrabBike.Api" --output none

az webapp config appsettings set \
  --resource-group "$RG" \
  --name "$API_APP" \
  --settings \
    ASPNETCORE_URLS="http://0.0.0.0:8080" \
    WEBSITES_PORT="8080" \
    SCM_DO_BUILD_DURING_DEPLOYMENT="false" \
    ENABLE_ORYX_BUILD="false" \
    Cors__AllowedOrigins="${CORS_ALLOWED_ORIGINS:-http://localhost:3000,https://${WEB_APP}.azurewebsites.net}" \
  --output none

bash "$(dirname "${BASH_SOURCE[0]}")/configure-observability.sh"

az webapp update --resource-group "$RG" --name "$API_APP" --set httpsOnly=true >/dev/null

az webapp config set \
  --resource-group "$RG" \
  --name "$WEB_APP" \
  --linux-fx-version "$web_runtime" \
  --always-on true \
  --startup-file "rm -rf .next/cache && node server.js" --output none

az webapp config appsettings set \
  --resource-group "$RG" \
  --name "$WEB_APP" \
  --settings \
    SCM_DO_BUILD_DURING_DEPLOYMENT="false" \
    ENABLE_ORYX_BUILD="false" \
    WEBSITES_PORT="8080" \
    PORT="8080" \
    HOSTNAME="0.0.0.0" \
    WEBSITES_CONTAINER_START_TIME_LIMIT="600" \
    API_BASE_URL="https://${API_APP}.azurewebsites.net" \
  --output none

az webapp update --resource-group "$RG" --name "$WEB_APP" --set httpsOnly=true >/dev/null

echo "API: https://${API_APP}.azurewebsites.net"
echo "Web: https://${WEB_APP}.azurewebsites.net"
