#!/usr/bin/env bash
# Checks that a version has its release notes: a section in CHANGELOG.md, and no rules left in
# AnalyzerReleases.Unshipped.md. Run it from the repository root: check-release-notes.sh <version>
set -euo pipefail

version="$1"
complete=true

if ! grep -qF "## [$version]" CHANGELOG.md 2> /dev/null; then
    echo "::error file=CHANGELOG.md::No section '## [$version]'. Turn 'Unreleased' into the version and the date in the change that sets the version."
    complete=false
fi

if grep -qE '^TDSL[0-9]+ ' src/TemplateDsl.Generators/AnalyzerReleases.Unshipped.md; then
    echo "::error file=src/TemplateDsl.Generators/AnalyzerReleases.Unshipped.md::Still lists rules. Move them to a '## Release $version' section of AnalyzerReleases.Shipped.md."
    complete=false
fi

if [ "$complete" = true ]; then
    echo "The release notes of $version are complete."
else
    exit 1
fi
