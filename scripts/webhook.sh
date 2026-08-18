#!/usr/bin/env bash
#
# Registers, inspects or removes the Telegram webhook.
#
#   scripts/webhook.sh set      point Telegram at $PUBLIC_BASE_URL/telegram/webhook
#   scripts/webhook.sh info     what Telegram thinks, including the last delivery error
#   scripts/webhook.sh delete   stop deliveries
#
# Reads TELEGRAM_BOT_TOKEN, WEBHOOK_SECRET_TOKEN and PUBLIC_BASE_URL from the
# environment, falling back to .env. The bot token is never printed: it is a
# credential, and the URL it goes in would put it in your shell history.
#
# Run `set` again every time the public URL changes — a new ngrok session gets a
# new hostname, and Telegram keeps posting to the old one until told otherwise.

set -euo pipefail

cd "$(dirname "$0")/.."

# Reads one key out of .env without sourcing it: connection strings are full of
# semicolons, and sourcing would hand them to the shell as commands.
env_value() {
  [[ -f .env ]] || return 0
  sed -n "s/^$1=//p" .env | tail -n 1
}

require() {
  local name="$1"
  local value="${!name:-}"

  if [[ -z "$value" ]]; then
    value="$(env_value "$name")"
  fi

  if [[ -z "$value" ]]; then
    echo "Missing $name. Set it in .env or in the environment." >&2
    exit 1
  fi

  printf '%s' "$value"
}

pretty() {
  if command -v python3 >/dev/null 2>&1; then
    python3 -m json.tool
  else
    cat
  fi
}

api() {
  local method="$1"
  shift

  curl -sS -X POST "https://api.telegram.org/bot${TOKEN}/${method}" "$@"
}

TOKEN="$(require TELEGRAM_BOT_TOKEN)"

case "${1:-set}" in
  set)
    SECRET="$(require WEBHOOK_SECRET_TOKEN)"
    BASE_URL="$(require PUBLIC_BASE_URL)"
    WEBHOOK_URL="${BASE_URL%/}/telegram/webhook"

    echo "Pointing the webhook at ${WEBHOOK_URL}"

    # allowed_updates: the router ignores everything that is not a message, so
    # there is no reason for Telegram to deliver the rest.
    api setWebhook \
      --data-urlencode "url=${WEBHOOK_URL}" \
      --data-urlencode "secret_token=${SECRET}" \
      --data-urlencode 'allowed_updates=["message"]' \
      --data-urlencode 'drop_pending_updates=true' | pretty
    ;;

  info)
    # last_error_message is the first place to look when nothing arrives: an
    # expired tunnel, a bad certificate and a 401 from a mismatched secret all
    # show up here.
    api getWebhookInfo | pretty
    ;;

  delete)
    api deleteWebhook --data-urlencode 'drop_pending_updates=false' | pretty
    ;;

  *)
    echo "Usage: scripts/webhook.sh [set|info|delete]" >&2
    exit 2
    ;;
esac
