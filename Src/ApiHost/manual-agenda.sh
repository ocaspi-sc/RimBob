#!/usr/bin/env bash
# Manual fallback: post a MayorAgendaInput JSON (from another LLM) to the running
# Host. The system prompt + last user message are in `logs/mayor-prompt-latest.md`.
#
# Usage:
#   ./manual-agenda.sh response.json        # paste from a file
#   pbpaste | ./manual-agenda.sh -          # pipe from clipboard (mac)
#   ./manual-agenda.sh                      # paste interactively, end with Ctrl-D
set -euo pipefail
URL="${RIMAI_URL:-http://localhost:5000}/api/agenda/manual"
if [[ $# -eq 1 && "$1" != "-" ]]; then
    BODY="@$1"
else
    BODY="@-"
fi
curl -fsS -X POST "$URL" -H 'Content-Type: application/json' --data-binary "$BODY"
echo
