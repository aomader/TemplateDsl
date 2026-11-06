#!/usr/bin/env bash
# Prints the release notes of a version: its section of CHANGELOG.md, without the heading. Run it from the repository
# root: release-notes.sh <version>. Prints nothing for a version without a section.
set -euo pipefail

version="$1"

# The section ends at the next version or at the link references below the last one. Blank lines around it are dropped.
awk -v heading="## [$version]" '
    index($0, heading) == 1 { inside = 1; next }
    inside && (/^## \[/ || /^\[[^]]*\]: /) { exit }
    inside && NF { while (blank > 0) { print ""; blank-- } started = 1; print; next }
    inside && started { blank++ }
' CHANGELOG.md
