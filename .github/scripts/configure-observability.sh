#!/usr/bin/env bash
# Called only by the manual deployment. Use core CLI commands, not the Insights extension.
set +x
set -euo pipefail

RG="${AZURE_RESOURCE_GROUP:-rg-grabbike-dev}"
API_APP="${AZURE_API_APP:-app-grabbike-api}"
LOCATION="${AZURE_LOCATION:-southeastasia}"
INSIGHTS="${AZURE_APPLICATION_INSIGHTS:-appi-grabbike-api}"
WORKSPACE="${AZURE_LOG_ANALYTICS_WORKSPACE:-log-grabbike}"
RETENTION_DAYS="${AZURE_LOG_RETENTION_DAYS:-30}"
DAILY_CAP_GB="${AZURE_LOG_DAILY_CAP_GB:-1}"

apply_api_settings() {
  local connection_string="$1"
  if [ "${GITHUB_ACTIONS:-false}" = "true" ]; then
    local masked_value="${connection_string//'%'/'%25'}"
    masked_value="${masked_value//$'\r'/'%0D'}"
    masked_value="${masked_value//$'\n'/'%0A'}"
    printf '::add-mask::%s\n' "$masked_value"
  fi

  # Never echo this value or the settings response. The web app receives no telemetry credentials.
  az webapp config appsettings set --resource-group "$RG" --name "$API_APP" \
    --settings APPLICATIONINSIGHTS_CONNECTION_STRING="$connection_string" \
      OTEL_SERVICE_NAME="happy-ride-api" --output none
}

if [ -n "${APPLICATIONINSIGHTS_CONNECTION_STRING:-}" ]; then
  apply_api_settings "$APPLICATIONINSIGHTS_CONNECTION_STRING"
  echo "API telemetry configured from the supplied connection string."
  exit 0
fi

# Listing distinguishes an absent resource from authorization/network errors: errors stop deployment.
insights_id="$(az resource list --resource-group "$RG" --name "$INSIGHTS" \
  --resource-type Microsoft.Insights/components --query '[0].id' --output tsv)"

if [ -z "$insights_id" ]; then
  workspace_id="$(az resource list --resource-group "$RG" --name "$WORKSPACE" \
    --resource-type Microsoft.OperationalInsights/workspaces --query '[0].id' --output tsv)"
  if [ -z "$workspace_id" ]; then
    echo "Creating Log Analytics workspace: $WORKSPACE"
    az monitor log-analytics workspace create \
      --resource-group "$RG" --workspace-name "$WORKSPACE" --location "$LOCATION" \
      --sku PerGB2018 --retention-time "$RETENTION_DAYS" --quota "$DAILY_CAP_GB" \
      --output none
    workspace_id="$(az monitor log-analytics workspace show \
      --resource-group "$RG" --workspace-name "$WORKSPACE" --query id --output tsv)"
  fi

  if [ -z "$workspace_id" ]; then
    echo "Log Analytics workspace ID was empty; refusing to create an unlinked Insights resource." >&2
    exit 1
  fi

  # Serialize values instead of interpolating them into JSON.
  properties="$(python3 - "$LOCATION" "$workspace_id" <<'PY'
import json
import sys

print(json.dumps({
    "location": sys.argv[1],
    "kind": "web",
    "properties": {
        "Application_Type": "web",
        "WorkspaceResourceId": sys.argv[2],
        "IngestionMode": "LogAnalytics",
    },
}))
PY
  )"
  echo "Creating Application Insights: $INSIGHTS"
  az resource create --resource-group "$RG" --name "$INSIGHTS" \
    --resource-type Microsoft.Insights/components --api-version 2020-02-02 \
    --is-full-object --properties "$properties" --output none
fi

# Reuse an existing component exactly as configured, including its current workspace.
workspace_id="$(az resource show --resource-group "$RG" --name "$INSIGHTS" \
  --resource-type Microsoft.Insights/components --api-version 2020-02-02 \
  --query properties.WorkspaceResourceId --output tsv)"
if [ -z "$workspace_id" ]; then
  echo "Application Insights $INSIGHTS is not workspace-based. Link it to a workspace before deploying." >&2
  exit 1
fi

connection_string="$(az resource show --resource-group "$RG" --name "$INSIGHTS" \
  --resource-type Microsoft.Insights/components --api-version 2020-02-02 \
  --query properties.ConnectionString --output tsv)"
if [ -z "$connection_string" ]; then
  echo "Application Insights returned an empty connection string." >&2
  exit 1
fi
apply_api_settings "$connection_string"
unset connection_string
echo "API telemetry configured: $INSIGHTS (workspace: $workspace_id)"
