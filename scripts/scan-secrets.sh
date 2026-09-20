#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# gitleaks secret scan via Docker (CI parity, no local gitleaks binary needed).
#
#   scripts/scan-secrets.sh                 # scan working tree
#   scripts/scan-secrets.sh --full-history  # scan all reachable commits
#
# Exit code 0 = no findings. The `&&` chain stops on the first detection.
# ---------------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="--no-git"
if [[ "${1:-}" == "--full-history" ]]; then
  MODE=""
fi

exec docker run --rm \
  -v "${REPO_ROOT}:/src" \
  -w /src \
  ghcr.io/gitleaks/gitleaks:latest \
  detect --source /src --report-format json --report-path /tmp/gitleaks-report.json ${MODE}