# GitHub: CI, images, evals and branch protection

The workflows live in `.github/workflows` (PLAN.md §15). This page covers what they do and the repository settings
they rely on. The settings are applied by hand in the repository settings; nothing here changes them automatically.

## Workflows

| Workflow | Runs on | What it does |
|---|---|---|
| `ci.yml` | every pull request, pushes to `main` | .NET restore, vulnerable-package check, Release build, all tests (Testcontainers PostgreSQL) with coverage; dashboard `npm ci`, `npm audit` (production, high and above), lint, type-check, unit tests with coverage, production build. Coverage summaries appear on the run page; reports are artifacts. |
| `docker.yml` | pushes to `main`; pull requests that touch `src/`, `web/`, `deploy/docker/`, `Directory.*` or `.dockerignore`; manual | Builds `tendersync-api`, `-worker`, `-migrate` and `-web`. On `main` it pushes them to `ghcr.io/<owner>/` tagged with the full SHA, `sha-<short>` and `latest`. Pull requests build without pushing. |
| `deploy-azure.yml` | manual (dev or prod); `v*` tags deploy prod | Bicep in `deploy/azure`, generated secrets in Key Vault, images imported from GHCR, migrate job, then the apps move to the new image and a smoke check runs. Signs in to Azure through OIDC with per-environment variables; setup in `docs/runbooks/azure.md`. |
| `evals.yml` | nightly 03:30 SAST; manual with a dollar cap | Live smoke test and classification precision/recall against `docs/golden/classification.jsonl`. Model calls stop at the cap (default $2). Skipped with a note when the `ANTHROPIC_API_KEY` secret is missing. |

Dependabot (`.github/dependabot.yml`) proposes weekly updates for NuGet, npm, GitHub Actions, Dockerfiles and
Compose images. Minor and patch updates come grouped, and versions must be a week old first. MediatR and
FluentAssertions majors stay pinned for licence reasons (ADR-0019), and Next.js majors are a planned upgrade.

## Branch protection for `main`

Settings → Rules → Rulesets → New branch ruleset, target `main`:

1. **Restrict deletions** and **Block force pushes**.
2. **Require a pull request before merging.** One approval is enough for a single maintainer to leave at 0; turn on
   **Dismiss stale approvals** if reviewers join.
3. **Require status checks to pass**, with **Require branches to be up to date**. Add these checks (names as the jobs
   report them):
   - `Build and test (.NET)`
   - `Build and test (web)`

   Do not add the `Image (...)` checks: `docker.yml` only runs when image inputs change, and a required check that never
   runs blocks the pull request.
4. **Require signed commits.** Commits here are signed with an SSH key (see [Commit signing](#commit-signing) below).
5. Optional: **Require linear history** if you prefer squash merges. The history so far uses merge commits.

## Commit signing

Commits are signed with an SSH key that is also added to GitHub as a **Signing key** (Settings → SSH and GPG keys). Set
it up once per clone:

```bash
git config gpg.format ssh
```

```bash
git config user.signingkey ~/.ssh/id_ed25519.pub
```

```bash
git config commit.gpgsign true
```

On Windows, start the OpenSSH Authentication Agent service, load the key with Windows' own `ssh-add`, and point Git at
Windows' `ssh-keygen` (`git config gpg.ssh.program C:/Windows/System32/OpenSSH/ssh-keygen.exe`). Otherwise Git for
Windows' bundled ssh cannot reach the agent. `git log --show-signature` checks signatures locally only when
`gpg.ssh.allowedSignersFile` is set; GitHub shows "Verified" either way.

## Repository settings the workflows need

- **Actions → General → Workflow permissions:** "Read repository contents" (the default). `docker.yml` asks for
  `packages: write` itself.
- **Secrets → Actions:** `ANTHROPIC_API_KEY` for `evals.yml`. Nothing else is needed; GHCR uses the built-in
  `GITHUB_TOKEN`.
- **Environments:** `dev` and `prod` hold the Azure identifiers for `deploy-azure.yml` as variables, not secrets.
  `prod` should have required reviewers. See `docs/runbooks/azure.md`.
- **Packages:** GHCR creates each package as private on its first push. To pull without signing in, open the package
  (Your profile → Packages), choose **Package settings → Change visibility**, and link it to this repository so the
  workflow keeps write access.
- **Code security:** turn on Dependabot alerts and security updates. Security updates bypass the grouping and
  cooldown.

## Running the published images

```bash
TENDERSYNC_REGISTRY=ghcr.io/ipelengmekgwe/ TENDERSYNC_TAG=latest docker compose -f deploy/docker/docker-compose.yml pull
```

```bash
TENDERSYNC_REGISTRY=ghcr.io/ipelengmekgwe/ TENDERSYNC_TAG=latest docker compose -f deploy/docker/docker-compose.yml up -d --no-build
```

Use a commit SHA instead of `latest` to pin a release. Private packages need `docker login ghcr.io` with a personal
access token that has `read:packages`.

## Running the checks locally

```bash
dotnet list TenderSync.slnx package --vulnerable --include-transitive
```

```bash
cd web && npm audit --omit=dev --audit-level=high && npm run test:coverage
```

```bash
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:1.7.12 -color
```

The last command lints the workflow files.
