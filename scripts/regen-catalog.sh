#!/usr/bin/env bash
#
# regen-catalog.sh — refresh catalog/apps.yaml from each app's latest GitHub release.
#
#   scripts/regen-catalog.sh                 # rewrite catalog/apps.yaml in place
#   scripts/regen-catalog.sh --app bbs       # only this app (repeatable)
#   scripts/regen-catalog.sh --check         # CI/pre-release drift guard: nonzero if stale
#   scripts/regen-catalog.sh --print         # print the version+sha block per app (no write)
#
# For each catalog entry it queries the app repo's LATEST release (`gh release view`), maps each
# RID to the matching release asset, pulls that asset's GitHub-recorded sha256 digest + download
# url, and rewrites the entry's version/url/sha256 IN PLACE — preserving comments, ordering, and
# name/description/icon/capabilities/homepage. A future bump is `scripts/regen-catalog.sh`, not
# hand-editing six hashes. The sha256 comes straight from GitHub's per-asset digest (no bytes are
# downloaded) — the same value build-deb.sh used to copy by hand. See docs/app-catalog.md § O3.
#
# Requires: gh (authenticated for the packet-net org), python3 with PyYAML.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
exec python3 "$here/regen-catalog.py" "$@"
