# Publishing `@packet-net/ax25-ts` and `ax25sdl` to npm

A step-by-step distribution guide for whoever publishes the first npm release. Aimed at .NET / NuGet veterans new to npm.

## What is npm?

[**npm**](https://www.npmjs.com/) is JavaScript's package registry — the equivalent of [**NuGet**](https://www.nuget.org/) in the .NET world. A few mental-model differences:

| Concept | NuGet | npm |
| --- | --- | --- |
| The registry | `nuget.org` | `npmjs.com` |
| Per-package identity | `Packet.Ax25` (PascalCase, dotted) | `@packet-net/ax25-ts` (lowercase, scoped) |
| "Scope" | n/a (everyone shares the flat namespace) | `@packet-net/...` — a scope is your username or an org; `@packet-net` is one for our project |
| Install a package | `dotnet add package Packet.Ax25` | `npm install @packet-net/ax25-ts` |
| Publish | `dotnet nuget push *.nupkg ...` | `npm publish --access public` |
| Auth token | `--api-key` flag or `~/.config/NuGet/...` | `~/.npmrc` `_authToken=` line, or `NPM_TOKEN` env var |
| Versioning | SemVer (same convention) | SemVer (same convention) |
| Lockfile | `packages.lock.json` (CPM) | `package-lock.json` |
| Per-machine cache | `~/.nuget/packages` | `~/.npm` |

Two packages from this repo are publishable to npm:

- **[`ts-spec/`](../../ts-spec/)** → npm name `ax25sdl` (no scope; "the SDL data structures"). Generated tables that the AX.25 v2.2 state machine walks.
- **[`web/ax25-ts/`](../../web/ax25-ts/)** → npm name `@packet-net/ax25-ts` (scoped under `@packet-net`). The browser-targeted library — depends on `ax25sdl`.

The order matters: `ax25sdl` must be on npmjs.com before `@packet-net/ax25-ts` can install it by version.

---

## One-time setup

### 1. Create an npm account

Sign up at <https://www.npmjs.com/signup>. Use the same email you use for GitHub if you want the org-membership UX to be tidy. Enable 2FA from <https://www.npmjs.com/settings/~/profile> (Account Settings → Two-Factor Authentication) — npm enforces 2FA on package publishes for accounts that have it enabled; "auth and writes" is the right mode.

### 2. Create the `@packet-net` org / scope

Two paths:

**Option A — personal scope (cheapest, simplest):** if you sign up as username `packet-net`, you automatically get `@packet-net` as your personal scope. Publish under `@packet-net/...` without creating an org.

**Option B — proper org:** at <https://www.npmjs.com/org/create>, create a free public org named `packet-net`. Add your personal account as a member. This is the path to take if multiple maintainers will eventually publish releases.

Either path makes `@packet-net/ax25-ts` a valid name you can publish.

`ax25sdl` is unscoped — it doesn't need a scope, it just needs to be a unique name on npmjs.com. As of writing, `ax25sdl` is not taken; if someone has grabbed it by the time you publish, prefix it with the `@packet-net` scope (`@packet-net/ax25sdl`) and update `web/ax25-ts/package.json`'s `dependencies` to match.

### 3. Generate an automation token

A token is the CI-friendly substitute for `npm login`. Generate one at <https://www.npmjs.com/settings/~/tokens>:

1. Click **Generate New Token**.
2. Pick **Granular Access Token** (preferred) or, if that option is unavailable, **Automation**. Granular tokens scope to specific packages and have an expiry; automation tokens skip 2FA prompts and are appropriate for CI.
3. For granular:
   - **Packages and scopes**: select `@packet-net/*` (or "All packages" if you also need it for `ax25sdl`) with **Read and write** permission.
   - **Expiration**: 365 days is the sweet spot; calendar-remind yourself to rotate.
4. Copy the token — it starts with `npm_...` and is shown **once only**. If you lose it, generate another and revoke the lost one.

### 4. Add the token as a GitHub secret

In the `M0LTE/packet.net` repo on GitHub:

1. **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.
2. Name: `NPM_TOKEN` (uppercase, no `$`).
3. Value: paste the token.
4. Click **Add secret**.

The publish workflow in `.github/workflows/npm-publish.yml` will use this secret automatically when a tag is pushed.

---

## Publishing manually (first-time validation)

Before the GitHub Actions workflow runs in anger, validate the publish flow locally. This is one-time, to make sure your account is set up correctly and the tarballs npm builds look right.

### Step 1 — log in

```sh
npm login
```

Opens a browser to authenticate. Or, if you have the token from step 3 above:

```sh
echo "//registry.npmjs.org/:_authToken=npm_xxxxxxxxxxxx" >> ~/.npmrc
```

### Step 2 — build and dry-run `ax25sdl`

```sh
cd ts-spec
npm ci
npm run build
npm pack --dry-run
```

`npm pack --dry-run` prints what would be in the tarball without uploading. Check that you see only `dist/**` files plus `package.json`, `README.md` — no `node_modules/`, no source. (We don't include `src/` in `files`, because the generated `dist/` output is what consumers import.)

### Step 3 — publish `ax25sdl`

```sh
cd ts-spec
npm publish --access public
```

`--access public` is required for unscoped packages on a registry account that defaults to private. Output should end with `+ ax25sdl@0.1.0`.

Verify at <https://www.npmjs.com/package/ax25sdl> — the page should render with your README.

### Step 4 — flip `web/ax25-ts`'s dependency from `file:` to a version

For local development, `web/ax25-ts/package.json` declares its dependency on `ax25sdl` as:

```json
"dependencies": {
  "ax25sdl": "file:../../ts-spec"
}
```

This `file:` reference is great in the monorepo — `npm install` symlinks straight to the local source. But npm **refuses to publish packages with `file:` dependencies** (they don't resolve outside the source tree). Before publishing, swap to a registry version range:

```sh
cd web/ax25-ts
npm pkg set dependencies.ax25sdl="^0.1.0"
npm install            # regenerates package-lock.json against the registry
```

> [!IMPORTANT]
> Don't commit this `file:` → `^0.1.0` flip to `main`. It breaks the local-dev flow until the next codegen run rebuilds `ts-spec/dist/`. The GitHub Actions workflow in `.github/workflows/npm-publish.yml` does this flip in CI before publishing, then `web/ax25-ts/package.json` on `main` keeps the `file:` reference.

### Step 5 — build and publish `@packet-net/ax25-ts`

```sh
cd web/ax25-ts
npm ci
npm run build
npm pack --dry-run        # sanity check
npm publish --access public
```

`--access public` is required for scoped packages on a free account (scoped packages default to private). Output should end with `+ @packet-net/ax25-ts@0.1.0`.

Verify at <https://www.npmjs.com/package/@packet-net/ax25-ts>.

### Step 6 — revert your local `file:` flip

```sh
cd web/ax25-ts
git checkout -- package.json package-lock.json
```

You're done. The published package depends on the registry version; the working tree is back to the local-dev `file:` reference.

---

## Publishing automatically via GitHub Actions

Once the manual first-time publish has validated everything, future releases ship via `.github/workflows/npm-publish.yml`. The workflow triggers on tags matching `v*`.

### Bumping version + tagging a release

1. **Update versions** in both `package.json` files. SemVer:
   - `0.1.0` → `0.1.1` for a bugfix.
   - `0.1.0` → `0.2.0` for a feature that doesn't break API.
   - `0.1.0` → `1.0.0` once the API is stable.
2. **Update `CHANGELOG.md`** — move the `UNRELEASED` heading to the new version with today's date; create a new `UNRELEASED` section above it.
3. **Commit** the version + changelog bump:

   ```sh
   git add ts-spec/package.json web/ax25-ts/package.json web/ax25-ts/CHANGELOG.md
   git commit -m "release: 0.2.0"
   ```

4. **Tag the commit and push**:

   ```sh
   git tag v0.2.0
   git push origin main --tags
   ```

5. **Watch the workflow** at <https://github.com/M0LTE/packet.net/actions> — the `npm-publish` job should appear and publish both packages.

### What the workflow does

In order:

1. Checks out the tagged commit.
2. Installs Node + builds `ts-spec` (`npm ci` + `npm run build`).
3. Publishes `ax25sdl` with `npm publish --access public`.
4. Flips `web/ax25-ts`'s `ax25sdl` dependency from `file:` to a registry version (`^<package.json version>`).
5. Installs + builds `web/ax25-ts`.
6. Publishes `@packet-net/ax25-ts` with `npm publish --access public`.

The flip in step 4 lives inside the CI shell session only — it doesn't get committed back to `main`. The committed `package.json` on `main` always carries the `file:` reference so monorepo developers' `npm install` continues to work without modification.

### Skipping the publish step

If `NPM_TOKEN` is missing (e.g. the secret hasn't been added yet, or you're running the workflow from a fork without credentials), the publish step is no-op'd via an `if:` check on the secret. The workflow still runs through build + dry-run-pack, so you find out about packaging issues without needing valid credentials.

---

## Troubleshooting

### "402 Payment Required" or "ENOTFOUND" on publish

You're trying to publish a scoped package without `--access public`. npm defaults scoped packages to private, which requires a paid plan. Add `--access public` to the `npm publish` command.

### "403 Forbidden" — package name unavailable

`ax25sdl` was probably taken by someone else between the time of writing and your publish attempt. Switch to `@packet-net/ax25sdl`:

1. `cd ts-spec && npm pkg set name=@packet-net/ax25sdl`
2. In `web/ax25-ts/package.json`, change `"dependencies": { "ax25sdl": ... }` to `"@packet-net/ax25sdl": ...`.
3. In `web/ax25-ts/src/**/*.ts`, search-and-replace `from "ax25sdl"` → `from "@packet-net/ax25sdl"`.
4. Republish.

### "ERESOLVE — peer dep" complaints

`@packet-net/ax25-ts` declares `ax25sdl` as a **regular** dependency, not a peer. If you see ERESOLVE during local dev, it usually means `ts-spec/dist/` is out of date — rebuild it:

```sh
cd ts-spec && npm run build
cd ../web/ax25-ts && rm -rf node_modules && npm install
```

### "Cannot publish over existing version"

npm doesn't let you re-publish a version that's already on the registry — even if you've fixed a bug since. Bump the patch (`0.1.0` → `0.1.1`) and republish. The old `0.1.0` stays around (immutable history).

### Local dev broke after a manual publish flow

You forgot step 6 — `git checkout -- web/ax25-ts/package.json web/ax25-ts/package-lock.json`. Restore the `file:` dependency reference and `npm install` will find `ts-spec/dist/` again.

---

## Reference

- [npm docs — publishing a package](https://docs.npmjs.com/cli/v10/commands/npm-publish)
- [npm docs — semver ranges](https://docs.npmjs.com/cli/v10/configuring-npm/package-json#dependencies)
- [Keep a Changelog](https://keepachangelog.com/) — the changelog format we follow.
- [SemVer 2.0.0 spec](https://semver.org/spec/v2.0.0.html)
- [npm 2FA docs](https://docs.npmjs.com/configuring-two-factor-authentication)
