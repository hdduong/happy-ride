#!/usr/bin/env bash
# Point the running API at Google or VietMap without publishing a new build.
# Key values stay out of the log. MAPS_PROVIDER is unchanged; the next deployment
# writes that variable back into Maps__Provider.
set +x
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
trap 'rm -f "$settings_file" "${settings_file}.previous"' EXIT
az webapp config appsettings list --resource-group "$rg" --name "$app" --output json >"$settings_file"
python3 - "$provider" "$settings_file" "${required[@]}" <<'PY'
import json
import sys

provider = sys.argv[1]
with open(sys.argv[2], encoding="utf-8") as handle:
    settings = {item["name"]: (item.get("value") or "").strip() for item in json.load(handle)}
missing = [name for name in sys.argv[3:] if not settings.get(name)]
current = settings.get("Maps__Provider") or "Fake"
print(f"Current Maps__Provider: {current}")
open(sys.argv[2] + ".previous", "w", encoding="utf-8").write(current)
if missing:
    print("Missing or empty: " + ", ".join(missing), file=sys.stderr)
    sys.exit(1)
print(f"{provider} map keys are present.")
PY
previous="$(cat "${settings_file}.previous")"
rm -f "${settings_file}.previous"

restore_previous() {
  echo "Restoring Maps__Provider to ${previous}." >&2
  az webapp config appsettings set \
    --resource-group "$rg" \
    --name "$app" \
    --settings "Maps__Provider=${previous}" \
    --output none
}

az webapp config appsettings set \
  --resource-group "$rg" \
  --name "$app" \
  --settings "Maps__Provider=$provider" \
  --output none
echo "Maps__Provider is now $provider"

echo "Waiting for the API to report ${provider}."
live=""
for _ in $(seq 1 20); do
  if live="$(curl -fsS "${web%/}/backend/api/map-config" | python3 -c 'import json,sys
try:
    print(json.load(sys.stdin).get("provider") or "")
except Exception:
    print("")')"; then
    echo "Live provider: ${live:-unknown}"
    if [ "$live" = "$provider" ]; then
      break
    fi
  else
    live=""
    echo "Map config is not ready yet."
  fi
  sleep 15
done
if [ "$live" != "$provider" ]; then
  restore_previous
  echo "The API did not report ${provider}." >&2
  exit 1
fi

echo "GitHub MAPS_PROVIDER was not changed. Set that repository variable and the production environment variable to ${provider}, or the next deployment writes the old provider back."
