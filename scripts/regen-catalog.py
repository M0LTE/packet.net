#!/usr/bin/env python3
"""regen-catalog.py — refresh catalog/apps.yaml from each app's latest GitHub release.

For every app in the catalog this:
  1. resolves the app's GitHub repo (catalog `homepage:`, falling back to a built-in map),
  2. queries its LATEST release with `gh release view --json tagName,assets`,
  3. maps each catalog RID to the matching release asset (by the asset's filename),
  4. pulls that asset's GitHub-recorded sha256 digest (`assets[].digest = "sha256:…"`) and
     download url, and
  5. rewrites the entry's `version:`, every artifact `url:`, and every `sha256:` IN PLACE —
     preserving all comments, ordering, name/description/icon/capabilities/homepage, and the
     three artifact kinds (assets/deb/pdnapp).

The sha256 pin comes straight from GitHub's per-asset digest — the same value `build-deb.sh`
used to hand-copy — so "vetted" still means "Tom ran regen against a chosen release". No bytes
are downloaded; the digest is authoritative metadata GitHub computes on upload.

This is idempotent: re-running with no upstream change rewrites the file to identical bytes.

Usage:
  scripts/regen-catalog.py [--catalog PATH] [--app ID ...] [--check] [--print]

  (no flags)     rewrite catalog/apps.yaml in place to the latest releases
  --app ID       only consider these app ids (repeatable); default: all
  --check        exit non-zero if the catalog is NOT already up to date; write nothing
                 (the CI / pre-release drift guard)
  --print        print the up-to-date version+sha block per app to stdout; write nothing
                 (the paste-friendly first-cut mode)
  --catalog P    operate on P instead of <repo>/catalog/apps.yaml

Requires: `gh` (authenticated), Python 3.10+ (stdlib + PyYAML for the read-only parse).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

try:
    import yaml  # read-only parse to learn structure; the WRITE is anchored text, not a redump.
except ImportError:
    sys.exit("regen-catalog: PyYAML is required (pip install pyyaml).")

# The three runtime ids the catalog pins, and how each maps onto an asset filename token.
# `assets` binaries are named `<tool>-<rid>` (dapps-linux-x64); `deb` artifacts use the Debian
# arch (linux-x64 → amd64, etc.). We accept either spelling per RID when matching an asset.
RID_DEB_ARCH = {"linux-x64": "amd64", "linux-arm64": "arm64", "linux-arm": "armhf"}

# Fallback id → "owner/repo" map, used only when an entry has no parseable GitHub `homepage:`.
DEFAULT_REPOS = {
    "dapps": "packet-net/dapps",
    "bpqchat": "packet-net/pdn-bpqchat",
    "convers": "packet-net/pdn-convers",
    "bbs": "packet-net/pdn-bbs",
}


def repo_for(entry: dict) -> str | None:
    home = (entry.get("homepage") or "").strip()
    m = re.match(r"https?://github\.com/([^/]+/[^/#?]+)", home)
    if m:
        return m.group(1).rstrip("/")
    return DEFAULT_REPOS.get(entry.get("id"))


def gh_latest_release(repo: str) -> dict:
    """The latest release of `repo` as {tag, assets:{name:{url,sha256}}}."""
    out = subprocess.run(
        ["gh", "release", "view", "--repo", repo, "--json", "tagName,assets"],
        capture_output=True, text=True, check=True,
    ).stdout
    data = json.loads(out)
    assets = {}
    for a in data.get("assets", []):
        digest = a.get("digest", "")
        sha = digest.split("sha256:", 1)[1] if digest.startswith("sha256:") else None
        assets[a["name"]] = {"url": a["url"], "sha256": sha}
    return {"tag": data["tagName"], "assets": assets}


def tag_to_version(tag: str) -> str:
    """Release tags are `vX.Y.Z`; the catalog `version:` is the bare `X.Y.Z`."""
    return tag[1:] if tag.startswith("v") else tag


def pick_asset(assets: dict, *tokens: str) -> dict | None:
    """The single asset whose name contains ALL the given tokens (most-specific first)."""
    matches = [a for name, a in assets.items() if all(t in name for t in tokens)]
    return matches[0] if len(matches) == 1 else None


def pick_suffix(assets: dict, suffix: str) -> dict | None:
    """The single asset whose name ENDS WITH `suffix` — used for `<tool>-<rid>` binaries where a
    plain substring match would catch `…-linux-arm` inside `…-linux-arm64`."""
    matches = [a for name, a in assets.items() if name.endswith(suffix)]
    return matches[0] if len(matches) == 1 else None


def resolve_pins(entry: dict, rel: dict) -> tuple[dict, list[str]]:
    """For one entry + its release, return {field-path: new-value} replacements and any problems.

    Keys: "version", ("manifest","url"/"sha256"), (rid,"url"/"sha256"). The values are looked up
    from the release assets by matching the asset filename to the RID/kind.
    """
    problems: list[str] = []
    pins: dict = {"version": tag_to_version(rel["tag"])}
    assets = rel["assets"]
    artifact = entry.get("artifact") or {}
    kind = artifact.get("kind")

    if kind == "assets":
        # manifest is a pdn-app.yaml asset; binaries are <tool>-<rid>.
        man = pick_asset(assets, "pdn-app.yaml")
        if man and man["sha256"]:
            pins[("manifest", "url")] = man["url"]
            pins[("manifest", "sha256")] = man["sha256"]
        else:
            problems.append("no unique pdn-app.yaml manifest asset")
        for rid in (artifact.get("binaries") or {}):
            a = pick_suffix(assets, "-" + rid) or pick_suffix(assets, rid)
            if a and a["sha256"]:
                pins[(rid, "url")] = a["url"]
                pins[(rid, "sha256")] = a["sha256"]
            else:
                problems.append(f"no unique asset for binary rid '{rid}'")

    elif kind == "deb":
        for rid in (artifact.get("debs") or {}):
            arch = RID_DEB_ARCH.get(rid, rid)
            a = pick_asset(assets, f"_{arch}.deb") or pick_asset(assets, arch, ".deb")
            if a and a["sha256"]:
                pins[(rid, "url")] = a["url"]
                pins[(rid, "sha256")] = a["sha256"]
            else:
                problems.append(f"no unique .deb asset for rid '{rid}' (arch {arch})")

    elif kind == "pdnapp":
        single = artifact.get("pdnapp")
        if single is not None:
            a = pick_asset(assets, ".pdnapp")
            if a and a["sha256"]:
                pins[("pdnapp", "url")] = a["url"]
                pins[("pdnapp", "sha256")] = a["sha256"]
            else:
                problems.append("no unique .pdnapp asset")
        for rid in (artifact.get("variants") or {}):
            a = pick_suffix(assets, f"-{rid}.pdnapp") or pick_asset(assets, rid, ".pdnapp")
            if a and a["sha256"]:
                pins[(rid, "url")] = a["url"]
                pins[(rid, "sha256")] = a["sha256"]
            else:
                problems.append(f"no unique .pdnapp variant for rid '{rid}'")
    else:
        problems.append(f"unknown artifact kind '{kind}'")

    return pins, problems


# ---- in-place, comment-preserving text rewrite -------------------------------------------------
#
# We do NOT redump the YAML (PyYAML strips the catalog's authored comments). Instead we find each
# app's text block by its `- id: <id>` line and rewrite, within that block, the FIRST `version:`
# and the `url:`/`sha256:` pair under each RID/manifest sub-key. The catalog's shape is regular
# (each rid is a 2-space-deeper mapping with a `url:` then `sha256:`), so anchored line edits are
# safe and exact.

ID_LINE = re.compile(r"^( *)- id:\s*(\S+)\s*$")
KEY_LINE = re.compile(r"^( *)([A-Za-z0-9_.-]+):\s*(.*)$")


def block_bounds(lines: list[str], app_id: str) -> tuple[int, int] | None:
    """The [start, end) line range of the `- id: app_id` list item."""
    start = None
    indent = None
    for i, ln in enumerate(lines):
        m = ID_LINE.match(ln)
        if m and m.group(2) == app_id:
            start = i
            indent = len(m.group(1))
            break
    if start is None:
        return None
    end = len(lines)
    for j in range(start + 1, len(lines)):
        m = ID_LINE.match(lines[j])
        if m and len(m.group(1)) == indent:
            end = j
            break
    return (start, end)


def rewrite_scalar(line: str, key: str, value: str) -> str:
    """Replace the scalar value of `key: …` on `line`, keeping indentation + any trailing comment."""
    m = KEY_LINE.match(line)
    assert m and m.group(2) == key, f"expected '{key}:' on: {line!r}"
    indent, _, rest = m.groups()
    comment = ""
    # Preserve a trailing ` # comment` if present and the value isn't itself quoted-with-hash.
    hash_idx = rest.find(" #")
    if hash_idx != -1 and not (rest[:hash_idx].count('"') % 2):
        comment = rest[hash_idx:]
    # version values in the catalog are double-quoted; urls/shas are bare.
    rendered = f'"{value}"' if key == "version" else value
    return f"{indent}{key}: {rendered}{comment}\n"


def apply_pins_to_block(lines: list[str], start: int, end: int, pins: dict) -> tuple[list[str], int]:
    """Apply `pins` to lines[start:end]; return (new_lines_segment, replacements_made)."""
    seg = lines[start:end]
    made = 0

    # 1) the single `version:` line (first one in the block, at entry depth).
    if "version" in pins:
        for i, ln in enumerate(seg):
            m = KEY_LINE.match(ln)
            if m and m.group(2) == "version":
                new = rewrite_scalar(ln, "version", pins["version"])
                if new != ln:
                    seg[i] = new
                    made += 1
                break

    # 2) for each tuple key (subkey, field), find the `subkey:` mapping header, then rewrite the
    #    `url:`/`sha256:` line that follows it (deeper indent), before the next same-or-shallower key.
    sub_targets: dict[str, dict[str, str]] = {}
    for k, v in pins.items():
        if isinstance(k, tuple):
            sub, field = k
            sub_targets.setdefault(sub, {})[field] = v

    for sub, fields in sub_targets.items():
        # locate the `<sub>:` header line.
        hdr = None
        hdr_indent = None
        for i, ln in enumerate(seg):
            m = KEY_LINE.match(ln)
            if m and m.group(2) == sub:
                hdr = i
                hdr_indent = len(m.group(1))
                break
        if hdr is None:
            continue
        # scan the sub-block (lines deeper than the header) for url:/sha256:.
        for j in range(hdr + 1, len(seg)):
            m = KEY_LINE.match(seg[j])
            if not m:
                continue
            if len(m.group(1)) <= hdr_indent:
                break  # left the sub-block.
            key = m.group(2)
            if key in fields:
                new = rewrite_scalar(seg[j], key, fields[key])
                if new != seg[j]:
                    seg[j] = new
                    made += 1

    return seg, made


def main() -> int:
    ap = argparse.ArgumentParser(description="Refresh catalog/apps.yaml from latest GitHub releases.")
    ap.add_argument("--catalog", type=Path, default=None)
    ap.add_argument("--app", action="append", dest="apps", default=None, help="restrict to these ids")
    ap.add_argument("--check", action="store_true", help="exit 1 if not already up to date; write nothing")
    ap.add_argument("--print", action="store_true", dest="print_only", help="print pins per app; write nothing")
    args = ap.parse_args()

    catalog_path = args.catalog or repo_root() / "catalog" / "apps.yaml"
    if not catalog_path.is_file():
        return fail(f"no catalog at {catalog_path}")

    original = catalog_path.read_text()
    lines = original.splitlines(keepends=True)
    doc = yaml.safe_load(original) or {}
    entries = doc.get("apps") or []
    want = set(args.apps) if args.apps else None

    total_made = 0
    any_problem = False
    for entry in entries:
        app_id = entry.get("id")
        if want is not None and app_id not in want:
            continue
        repo = repo_for(entry)
        if not repo:
            print(f"  {app_id}: SKIP (no GitHub repo resolvable)", file=sys.stderr)
            any_problem = True
            continue

        try:
            rel = gh_latest_release(repo)
        except subprocess.CalledProcessError as e:
            print(f"  {app_id}: gh failed for {repo}: {e.stderr.strip()}", file=sys.stderr)
            any_problem = True
            continue

        pins, problems = resolve_pins(entry, rel)
        for p in problems:
            print(f"  {app_id}: {p}", file=sys.stderr)
            any_problem = True

        if args.print_only:
            print(f"# {app_id} → {repo} @ {rel['tag']}")
            print(f'  version: "{pins.get("version", "?")}"')
            for k, v in pins.items():
                if isinstance(k, tuple) and k[1] == "sha256":
                    print(f"  {k[0]}.sha256: {v}")
            print()
            continue

        bounds = block_bounds(lines, app_id)
        if bounds is None:
            print(f"  {app_id}: could not locate its block in the catalog text", file=sys.stderr)
            any_problem = True
            continue
        start, end = bounds
        seg, made = apply_pins_to_block(lines, start, end, pins)
        lines[start:end] = seg
        if made:
            print(f"  {app_id} → {repo} @ {rel['tag']} ({made} pin(s) updated)")
        else:
            print(f"  {app_id} → {repo} @ {rel['tag']} (already current)")
        total_made += made

    new_text = "".join(lines)

    if args.print_only:
        return 1 if any_problem else 0

    if args.check:
        if new_text != original:
            print("catalog is OUT OF DATE — run scripts/regen-catalog.sh", file=sys.stderr)
            return 1
        print("catalog is up to date.")
        return 1 if any_problem else 0

    if new_text != original:
        catalog_path.write_text(new_text)
        print(f"wrote {catalog_path} ({total_made} pin(s) updated).")
    else:
        print(f"{catalog_path} already up to date — no change.")
    return 1 if any_problem else 0


def repo_root() -> Path:
    d = Path(__file__).resolve().parent
    while d != d.parent:
        if (d / "catalog" / "apps.yaml").is_file():
            return d
        d = d.parent
    return Path(__file__).resolve().parent.parent


def fail(msg: str) -> int:
    print(f"regen-catalog: {msg}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
