#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "TalentSuite local launcher"
echo "Repository: $ROOT_DIR"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK is not installed or not on PATH."
  exit 1
fi

if ! command -v aspire >/dev/null 2>&1; then
  echo "ERROR: Aspire CLI is not installed or not on PATH."
  echo "Install with: dotnet tool install -g Aspire.Cli"
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: Docker is not installed or not on PATH."
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  echo "ERROR: Docker is not running. Start Docker Desktop and retry."
  exit 1
fi

export TALENTSUITE_INFRA_MODE="${TALENTSUITE_INFRA_MODE:-local}"

echo "Running full local stack with TALENTSUITE_INFRA_MODE=$TALENTSUITE_INFRA_MODE"
echo "Press Ctrl+C to stop."

exec aspire run
