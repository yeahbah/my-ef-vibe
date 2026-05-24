#!/usr/bin/env bash
# Prints the next patch version (e.g. 0.1.4) by taking the max of git tags,
# NuGet.org, and src/MyEfVibe/MyEfVibe.csproj, then bumping patch.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

bump_patch() {
  local ver="${1%%-*}"
  local major minor patch
  IFS=. read -r major minor patch <<<"$ver"
  patch=$((patch + 1))
  echo "${major}.${minor}.${patch}"
}

latest_from_git() {
  git tag -l 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname 2>/dev/null | head -n1 | sed 's/^v//' || true
}

latest_from_nuget() {
  local json
  if json="$(curl -fsSL "https://api.nuget.org/v3-flatcontainer/efvibe/index.json" 2>/dev/null)"; then
    echo "$json" | jq -r '.versions[]?' 2>/dev/null | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n1 || true
  fi
}

latest_from_csproj() {
  grep -oP '(?<=<Version>)[^<]+' src/MyEfVibe/MyEfVibe.csproj 2>/dev/null | head -n1 || echo "0.1.0"
}

latest_from_vscode_extension() {
  jq -r '.version // empty' vscode-extension/package.json 2>/dev/null || true
}

G="$(latest_from_git)"
N="$(latest_from_nuget)"
C="$(latest_from_csproj)"
V="$(latest_from_vscode_extension)"

BASE="$(printf '%s\n%s\n%s\n%s\n' "$G" "$N" "$C" "$V" | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n1)"
if [[ -z "$BASE" ]]; then
  BASE="0.1.0"
fi

bump_patch "$BASE"
