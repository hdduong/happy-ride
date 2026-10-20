#!/usr/bin/env bash
# Wait until a workflow run for this commit on main has succeeded.
set -euo pipefail

sha="${1:?commit sha required}"
workflow="${2:?workflow file required}"
attempts="${3:-45}"

for _ in $(seq 1 "$attempts"); do
  row="$(gh run list --workflow "$workflow" --commit "$sha" --limit 20 \
    --json event,headBranch,status,conclusion \
    --jq '[.[] | select(.event=="push" and .headBranch=="main")][0] // {}')"
  status="$(echo "$row" | jq -r '.status // empty')"
  conclusion="$(echo "$row" | jq -r '.conclusion // empty')"
  echo "$workflow status=${status:-missing} conclusion=${conclusion:-none}"

  case "$conclusion" in
    success)
      exit 0
      ;;
    failure|cancelled|timed_out|startup_failure)
      echo "$workflow $conclusion for $sha"
      exit 1
      ;;
  esac

  sleep 20
done

echo "Timed out waiting for $workflow on $sha"
exit 1
