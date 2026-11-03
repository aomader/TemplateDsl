#!/usr/bin/env bash
# Prints the version of the packages, Version in Directory.Build.props, at a commit, by default HEAD.
# Prints nothing for a commit that doesn't exist or has no version.
set -euo pipefail

props="$(git show "${1:-HEAD}:Directory.Build.props" 2> /dev/null || true)"
sed -n 's|^[[:space:]]*<Version>\(.*\)</Version>.*|\1|p' <<< "$props"
