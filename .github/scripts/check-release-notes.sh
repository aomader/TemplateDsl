#!/usr/bin/env bash
# Checks that a version has its release notes. Run it from the repository root: check-release-notes.sh <version>
#
# Every version needs a section in CHANGELOG.md. A stable version, such as 0.1.0, also ships its diagnostics: they move
# from AnalyzerReleases.Unshipped.md to a '## Release 0.1.0' section of AnalyzerReleases.Shipped.md. A preview, such
# as 0.1.0-preview.1, leaves them unshipped, because the release headers of those files only take plain version
# numbers (RS2007), and its diagnostics ship with the stable version that follows. Rules deleted instead of moved are
# caught by the build itself, since every diagnostic has to be in one of the two files.
set -euo pipefail

version="$1"
complete=true
analyzers=src/TemplateDsl.Generators

if ! grep -qF "## [$version]" CHANGELOG.md 2> /dev/null; then
    echo "::error file=CHANGELOG.md::No section '## [$version]'. Turn 'Unreleased' into the version and the date in the change that sets the version."
    complete=false
fi

case "$version" in
    *-*)
        ;;
    *)
        if grep -qE '^TDSL[0-9]+ ' "$analyzers/AnalyzerReleases.Unshipped.md"; then
            echo "::error file=$analyzers/AnalyzerReleases.Unshipped.md::Still lists rules. Move them to a '## Release $version' section of AnalyzerReleases.Shipped.md."
            complete=false
        fi
        ;;
esac

if [ "$complete" = true ]; then
    echo "The release notes of $version are complete."
else
    exit 1
fi
