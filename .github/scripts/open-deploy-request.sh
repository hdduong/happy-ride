#!/usr/bin/env bash
# Open the GitHub issue that asks a person to press the Deployment workflow button.
set -euo pipefail

: "${VERSION:?}"
: "${SHA:?}"
: "${REPO:?}"

body_file="$(mktemp)"
cat >"$body_file" <<EOF
Build and CI passed for \`${SHA}\`.

Version: **${VERSION}**

This deploy adds or updates one Linux B1 App Service plan, \`plan-grabbike\`, in \`rg-grabbike-dev\`. The plan is about \$13 per month and is shared by the API and the web. The PostgreSQL server already in that group is left as it is.

- API: https://app-grabbike-api.azurewebsites.net
- Web: https://app-grabbike-web.azurewebsites.net

To deploy, open the [Deployment workflow](https://github.com/${REPO}/actions/workflows/deploy.yml), click **Run workflow**, check **Deploy to Azure production**, and click the green **Run workflow** button. Leave the branch set to \`main\`.
EOF

gh label create deployment-request \
  --color 1D76DB \
  --description "Production deploy is waiting for the Run workflow button" \
  --force

for number in $(gh issue list --label deployment-request --state open --json number --jq '.[].number'); do
  gh issue comment "$number" --body "Superseded by ${VERSION} (${SHA})."
  gh issue close "$number" --reason "not planned"
done

url="$(gh issue create \
  --title "Deploy ${VERSION} to Azure production" \
  --label deployment-request \
  --body-file "$body_file")"
number="${url##*/}"
gh issue edit "$number" --add-assignee hdduong || echo "Could not assign hdduong; the issue is still open."
echo "Opened ${url}"
