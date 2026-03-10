#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "bootstrap-rbac.sh failed at line $LINENO"' ERR

usage() {
  cat <<'EOF'
Usage:
  scripts/ci/bootstrap-rbac.sh --assignee-object-id <object-id> [options]

Options:
  --assignee-object-id <id>   Required. Service principal object id.
  --subscription-id <id>      Optional. Defaults to AZURE_SUBSCRIPTION_ID or current az account.
  --resource-group <name>     Optional. Defaults to rg-${AZURE_ENV_NAME} when scope is resource group.
  --scope <resource-group|subscription|custom>
                              Optional. Default: resource-group
  --custom-scope <scope-id>   Required when --scope custom.
  -h, --help                  Show help.

Examples:
  scripts/ci/bootstrap-rbac.sh \
    --assignee-object-id 14c5b559-7825-4913-aa19-a714730a7846 \
    --subscription-id 00000000-0000-0000-0000-000000000000 \
    --resource-group rg-dev

  scripts/ci/bootstrap-rbac.sh \
    --assignee-object-id 14c5b559-7825-4913-aa19-a714730a7846 \
    --subscription-id 00000000-0000-0000-0000-000000000000 \
    --scope subscription
EOF
}

ASSIGNEE_OBJECT_ID=""
SUBSCRIPTION_ID="${AZURE_SUBSCRIPTION_ID:-}"
RESOURCE_GROUP=""
SCOPE_KIND="resource-group"
CUSTOM_SCOPE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --assignee-object-id)
      ASSIGNEE_OBJECT_ID="${2:-}"
      shift 2
      ;;
    --subscription-id)
      SUBSCRIPTION_ID="${2:-}"
      shift 2
      ;;
    --resource-group)
      RESOURCE_GROUP="${2:-}"
      shift 2
      ;;
    --scope)
      SCOPE_KIND="${2:-}"
      shift 2
      ;;
    --custom-scope)
      CUSTOM_SCOPE="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1"
      usage
      exit 1
      ;;
  esac
done

if [ -z "$ASSIGNEE_OBJECT_ID" ]; then
  echo "Missing required argument: --assignee-object-id"
  exit 1
fi

if [ -z "$SUBSCRIPTION_ID" ]; then
  SUBSCRIPTION_ID="$(az account show --query id -o tsv 2>/dev/null || true)"
fi
test -n "$SUBSCRIPTION_ID" || (echo "Unable to resolve subscription id. Set --subscription-id or AZURE_SUBSCRIPTION_ID." && exit 1)

if [ -z "$RESOURCE_GROUP" ] && [ -n "${AZURE_ENV_NAME:-}" ]; then
  RESOURCE_GROUP="rg-${AZURE_ENV_NAME}"
fi

case "$SCOPE_KIND" in
  resource-group)
    test -n "$RESOURCE_GROUP" || (echo "Missing --resource-group (or AZURE_ENV_NAME) for resource-group scope." && exit 1)
    SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}"
    ;;
  subscription)
    SCOPE="/subscriptions/${SUBSCRIPTION_ID}"
    ;;
  custom)
    test -n "$CUSTOM_SCOPE" || (echo "Missing --custom-scope when --scope custom is used." && exit 1)
    SCOPE="$CUSTOM_SCOPE"
    ;;
  *)
    echo "Invalid --scope value: $SCOPE_KIND"
    exit 1
    ;;
esac

az account set --subscription "$SUBSCRIPTION_ID"

ensure_role_assignment() {
  local role_name="$1"
  local existing_id

  existing_id="$(az role assignment list \
    --assignee-object-id "$ASSIGNEE_OBJECT_ID" \
    --scope "$SCOPE" \
    --role "$role_name" \
    --query '[0].id' -o tsv 2>/dev/null || true)"

  if [ -n "$existing_id" ]; then
    echo "Role already assigned: ${role_name}"
    return 0
  fi

  az role assignment create \
    --assignee-object-id "$ASSIGNEE_OBJECT_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "$role_name" \
    --scope "$SCOPE" >/dev/null

  echo "Assigned role: ${role_name}"
}

echo "Bootstrapping RBAC on scope: $SCOPE"
echo "Assignee object id: $ASSIGNEE_OBJECT_ID"

ensure_role_assignment "Contributor"

if ! ensure_role_assignment "User Access Administrator"; then
  echo "User Access Administrator assignment failed. Trying Role Based Access Control Administrator."
  ensure_role_assignment "Role Based Access Control Administrator"
fi

echo "RBAC bootstrap complete."
