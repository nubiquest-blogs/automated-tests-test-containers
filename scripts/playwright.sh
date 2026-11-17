#!/usr/bin/env sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cli="$root/tests/web/Todo.AutomatedTests/bin/Debug/net10.0/.playwright/package/cli.js"
if [ ! -f "$cli" ]; then
  echo "Build the solution first: dotnet build Todo.slnx" >&2
  exit 1
fi
# Use the CLI bundled with Microsoft.Playwright, so browser and driver versions match.
exec node "$cli" "$@"
