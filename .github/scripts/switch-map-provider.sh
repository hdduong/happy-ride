#!/usr/bin/env bash
# Point the running API at Google or VietMap and record that choice for the next deploy.
# Key values stay out of the log.
set -euo pipefail

provider="${PROVIDER:-}"
rg="${AZURE_RESOURCE_GROUP:-rg-grabbike-dev}"
app="${AZURE_API_APP:-app-grabbike-api}"
web="${WEB_BASE_URL:-https://app-grabbike-web.azurewebsites.net}"

case "$provider" in
  Google)
    required=(Maps__Google__ApiKey Maps__Google__BrowserKey)
    ;;
  VietMap)
    required=(Maps__VietMap__ApiKey Maps__VietMap__TileKey)
    ;;
  *)
    echo "Provider must be Google or VietMap." >&2
    exit 1
    ;;
esac

umask 077
settings_file="$(mktemp)"
trap 'rm -f "$settings_file"' EXIT
az webapp config appsettings list --resource-group "$rg" --name "$app" --output json >"$settings_file"
python3 - "$provider" "$settings_file" "${required[@]}" <<'PY'
import json
import sys

provider = sys.argv[1]
with open(sys.argv[2], encoding="utf-8") as handle:
    settings = {item["name"]: (item.get("value") or "").strip() for item in json.load(handle)}
missing = [name for name in sys.argv[3:] if not settings.get(name)]
print(f"Current Maps__Provider: {settings.get('Maps__Provider') or '(unset)'}")
if missing:
    print("Missing or empty: " + ", ".join(missing), file=sys.stderr)
    sys.exit(1)
print(f"{provider} map keys are present.")
PY

az webapp config appsettings set \
  --resource-group "$rg" \
  --name "$app" \
  --settings "Maps__Provider=$provider" \
  --output none
echo "Maps__Provider is now $provider"

echo "Waiting for the API to report ${provider}."
for _ in $(seq 1 20); do
  if body="$(curl -fsS "${web%/}/backend/api/map-config")"; then
    live="$(printf '%s' "$body" | python3 -c 'import json,sys
try:
    print(json.load(sys.stdin).get("provider") or "")
except Exception:
    print("")')"
    echo "Live provider: ${live:-unknown}"
    if [ "$live" = "$provider" ]; then
      break
    fi
  else
    echo "Map config is not ready yet."
  fi
  sleep 15
done
if [ "${live:-}" != "$provider" ]; then
  echo "The API did not report ${provider}." >&2
  exit 1
fi

update_variable() {
  local url="$1"
  local label="$2"
  local code
  code="$(curl -sS -o /tmp/map-variable.json -w "%{http_code}" \
    -X PATCH \
    -H "Authorization: Bearer ${GH_TOKEN}" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "$url" \
    -d "{\"name\":\"MAPS_PROVIDER\",\"value\":\"${provider}\"}")"
  if [ "$code" = "204" ]; then
    echo "Updated ${label} MAPS_PROVIDER."
    return 0
  fi
  echo "Could not update ${label} MAPS_PROVIDER (HTTP ${code})." >&2
  python3 - <<'PY'
import pathlib
text = pathlib.Path("/tmp/map-variable.json").read_text()
print(text[:300])
PY
  return 1
}

repo="${GITHUB_REPOSITORY:?}"
update_variable "https://api.github.com/repos/${repo}/actions/variables/MAPS_PROVIDER" "repository"
update_variable "https://api.github.com/repos/${repo}/environments/production/variables/MAPS_PROVIDER" "production"
