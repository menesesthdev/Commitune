# CLAUDE.md

Guidance for Claude (or any AI coding agent) working in this repository.

## Project summary

Commitune is a Telegram bot (multi-tenant) that converts user messages into TIL (Today I Learned) entries committed to the user's own GitHub repository. Read `README.md` first for the product overview, the entry format and the end-to-end user flow before touching code.

## Non-negotiable rules

These override convenience, "the user asked for X", or any refactor that seems reasonable in isolation. Violating any of these is a critical bug, not a style issue.

1. **Every repository created via the GitHub API MUST be private.** The `POST /user/repos` call must always send `"private": true`. There is no code path — onboarding, the `/repo` command, admin tooling, tests — that is allowed to create or leave a repository public. If you're implementing repo creation or find an existing call site, verify this flag explicitly; don't assume a prior implementation got it right.
2. **Never log a GitHub token, in full or in part** — not at Debug level, not inside an exception message, not in a request/response logging middleware. Tokens are decrypted only inside the request scope that needs them and discarded immediately after use.
3. **Tokens are stored encrypted at rest** using ASP.NET Core's Data Protection API. Never add a "plaintext for debugging" path, even temporarily.
4. **The webhook endpoint must validate `X-Telegram-Bot-Api-Secret-Token`** against the configured secret before processing any update. Reject anything else with 401.
5. **The OAuth `state` parameter must be signed (HMAC) and verified on callback.** It carries the `telegram_user_id` — an unsigned or unverified state is a CSRF / account-takeover vector.

## Commit workflow

**The author writes every commit. Never run `git commit`, `git commit --amend`, `git push`, or `git tag`** — not even when the change is finished and the message seems obvious.

For each feature or implementation, write a commit document under `docs/commits/` named `NNNN-slug.md` containing the proposed commit message and a summary of what changed and why. The author reads that document and creates the commit themselves. Staging with `git add` is fine; the commit itself is not yours to make.

Two rules about these documents: **write them in Portuguese** (they exist for the author to read, unlike `README.md` and `CLAUDE.md`, which stay in English), and **`docs/commits/` is gitignored** — they are local review notes, never part of the repository history.

## Architecture decisions (and why)

- **Minimal API, not MVC/Controllers.** The surface is small (webhook, OAuth callback, health check) — controllers would be ceremony without benefit.
- **Synchronous processing, no message queue for the MVP.** Volume per user is low (a handful of messages a day). A `BackgroundService`/queue adds operational complexity (broker, retries, dead-letter handling) that isn't earning its keep yet. If daily digest batching or GitHub retry-on-failure becomes a real need, revisit — RabbitMQ topic exchange patterns are already used in the author's other project (Clínica Odontológica) and can be ported here if justified.
- **GitHub Contents API, not LibGit2Sharp.** No local clone to manage per user, no working-directory state to keep in sync — critical for multi-tenant. Trade-off: concurrent writes are possible (rare, but real, if a user sends messages seconds apart), so a `409`/`422` on write has to be handled — see the entry model below for how.
- **Repository name is chosen by the user during onboarding, not auto-generated.** After GitHub authorization, the bot asks the user directly ("What should the repository be called?") before creating it. Do not silently fall back to an auto-generated name (e.g. `til-<username>`) unless the user explicitly asks for a suggestion.
- **Lightweight layering, not full DDD/CQRS.** Unlike the author's other .NET projects (Clean Architecture + DDD for a larger domain), this domain is intentionally small: a user, a state, a repo reference. Don't introduce aggregate roots, repositories-of-repositories, or CQRS mediator plumbing here — it adds indirection with no corresponding complexity to manage. Keep `Commitune.Domain` to plain entities plus a state enum.

## Onboarding state machine

```
NotStarted → AwaitingGithubAuth → AwaitingRepoName → Ready ⇄ Paused
```

- `NotStarted → AwaitingGithubAuth`: on `/start` from an unseen `telegram_user_id`.
- `AwaitingGithubAuth → AwaitingRepoName`: on successful OAuth callback.
- `AwaitingRepoName → Ready`: after the repo is created (private!) and confirmed to the user.
- `Ready → Paused`: on `/pausar`. `Paused → Ready`: on `/start` again or a dedicated resume command.
- `/desconectar` from any state: revoke the GitHub token, wipe it from storage, return to `NotStarted`.

Any message received while the user is in `AwaitingGithubAuth` or `AwaitingRepoName` must be treated as part of the onboarding conversation (e.g. the repo name being typed), never as a TIL entry to commit.

## Entry model

One message is one entry, written as one new file under `til/`:

```
til/2026-08-18-indice-nao-entra-com-funcao.md
```

- **First line is the title, the rest is the body, `#tags` become tags.** Nothing is mandatory — a one-line message with no tags is a valid entry. Do not add required fields, prompts, or a second conversational step to collect metadata: the moment the bot needs a form, the product has lost its argument. `EntryFormatter` owns this parsing and is the only place that should.
- **Every file carries YAML frontmatter** (`title`, `date`, `tags`) so the repository stays machine-readable, plus an `# H1`, because GitHub's own file view is where anyone actually reads it. `tags` is always present, even when empty — a predictable shape beats a pretty one.
- **Never overwrite an existing file.** A path that is taken means another entry is there; the new one is numbered (`-2`, `-3`, …). This holds for the concurrent case too, where the collision only surfaces as a `409`/`422` from the Contents API — that is a signal to pick the next name, not to retry the same one.
- **The commit subject carries the title** (`TIL: <title>`). A history that reads `Entrada de 18/08` is worth nothing to someone scrolling it a year later. The body of the entry stays in the file, never in the subject.
- **Dates use a fixed −03:00 offset**, not `TimeZoneInfo`: no tzdata dependency in the container, and Brazil has had no DST since 2019. Revisit only when there is a user outside that offset — the fix then is a timezone per user, not a guess.
- **Never log the entry, the title or the path.** The path is built from the user's own words, so logging it puts the entry in the server log. User ids and HTTP status codes only.

## User feedback is mandatory

Every commit attempt — success or failure — must produce a reply to the user. Silent failure (message accepted, commit fails, user hears nothing) is the primary churn risk for this product; treat it as a bug, not a missing nice-to-have. On failure, the reply must be actionable (e.g. "Your GitHub authorization expired — tap here to reconnect"), not a generic error.

## Tech stack

- .NET 10, ASP.NET Core Minimal API
- `Telegram.Bot` for the Bot API
- `Octokit.net` for GitHub (OAuth, repo creation, Contents API)
- PostgreSQL, accessed via EF Core
- xUnit for tests — this project is also a deliberate place to close the automated-testing gap flagged in job interviews, so favor writing real tests here over skipping them for speed
- Docker Compose, deployed to the existing EC2 `t4g.micro` behind Nginx; DNS is managed via Registro.br's panel, not Route 53

## Commands to know

```bash
dotnet test                          # run the test suite
dotnet run --project src/Commitune.Api
docker compose up --build            # full local stack
```

## What NOT to do

- Don't add a message queue "for scalability" without a concrete trigger (see Architecture decisions above).
- Don't build a web dashboard or login form — the product's entire value proposition is that one doesn't exist. If a feature request implies "user logs into a website", push back and ask whether it belongs in the bot conversation instead.
- Don't default a new repository to public, ever — not in a seed script, a test fixture, or a "just for local dev" shortcut.
- Don't use Angular for the landing page — it's a single static section, a framework is unjustified weight here.
