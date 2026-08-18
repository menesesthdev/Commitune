# Commitune

> Turn what you learn into a TIL log on GitHub. No sign-up. No dashboard. Just talk to a bot.

Commitune is a Telegram bot that turns every message you send into a **TIL** (Today I Learned) entry committed to your own GitHub repository. There's no website registration and no login form — you connect your GitHub account directly inside the Telegram conversation, choose a repository name, and start writing. Each message becomes a dated, tagged Markdown file, and a commit whose subject is what you learned.

## How it works

1. **Start the bot** — open the bot on Telegram (deep link from the landing page) and send `/start`.
2. **Connect GitHub** — tap the inline button, authorize Commitune on GitHub's own consent screen (one tap, revocable anytime).
3. **Name your repository** — tell the bot what to call it. Commitune creates it for you — **always as a private repository**.
4. **Write what you learned** — the first line becomes the title, the rest becomes the body, and any `#tag` becomes a tag.
5. **Watch it add up** — a searchable log of everything you learned, and a contribution graph that fills in as you go.

No dashboard. No password. The bot *is* the product.

## What an entry looks like

A message like this:

```
Índice não entra quando a coluna tem função
No Postgres, WHERE lower(email) = ... ignora o índice de email.
Precisa de índice na expressão. #postgres #indices
```

becomes `til/2026-08-18-indice-nao-entra-quando-a-coluna-tem-funcao.md`:

```markdown
---
title: "Índice não entra quando a coluna tem função"
date: 2026-08-18T22:30:00-03:00
tags: [postgres, indices]
---

# Índice não entra quando a coluna tem função

No Postgres, WHERE lower(email) = ... ignora o índice de email.
Precisa de índice na expressão.
```

committed as `TIL: Índice não entra quando a coluna tem função`.

Nothing about the convention is mandatory: a one-line message with no tags is a perfectly good entry. Two entries about the same topic on the same day get their own files (`-2`, `-3`, …) — an entry is never written over another.

## Why private repos only

Commitune never creates a public repository, regardless of what the user asks for. A learning log starts as personal notes, and notes are private by default and by design — this is a hard rule enforced at the API call level, not a UI default that can be silently skipped. See [`CLAUDE.md`](./CLAUDE.md) for the exact enforcement rule.

If you want the log to be a public portfolio, flip the repository's visibility yourself on GitHub. That is a decision with consequences — it publishes everything already written — so it stays with the person who wrote the entries.

## Architecture

```
Telegram ──(webhook)──▶ Commitune.Api ──▶ PostgreSQL (user state)
                              │
                              ▼
                       GitHub REST API
                    (Contents API — create/update file)
```

- **API**: ASP.NET Core Minimal API (.NET 10)
- **Telegram integration**: `Telegram.Bot`
- **GitHub integration**: `Octokit.net`
- **Database**: PostgreSQL + EF Core
- **Token encryption**: ASP.NET Core Data Protection API
- **Landing page**: static HTML/CSS, no build step
- **Infra**: Docker Compose on a single EC2 instance, Nginx reverse proxy, Let's Encrypt via Certbot

Messages are processed synchronously — no message queue in the MVP. See [`CLAUDE.md`](./CLAUDE.md) for the reasoning.

## Project structure

```
commitune/
├── src/
│   ├── Commitune.Api/             # Minimal API — webhook, OAuth callback, endpoints
│   ├── Commitune.Domain/          # Entities, value objects, onboarding state machine
│   ├── Commitune.Infrastructure/  # Postgres repository, GitHub client, Telegram client
│   └── Commitune.Tests/           # xUnit
├── landing/                       # Static landing page
├── docker-compose.yml
├── CLAUDE.md
└── README.md
```

## Running locally

```bash
# 1. Clone
git clone https://github.com/NICHOLAST0RRES/commitune.git
cd commitune

# 2. Configure environment variables
cp .env.example .env
# fill in: TELEGRAM_BOT_TOKEN, GITHUB_CLIENT_ID, GITHUB_CLIENT_SECRET,
# POSTGRES_CONNECTION_STRING, DATA_PROTECTION_KEY_PATH, WEBHOOK_SECRET_TOKEN

# 3. Run
docker compose up --build
```

You'll also need a public HTTPS URL for the Telegram webhook during local development (e.g. `ngrok http 5000`) — Telegram will not deliver updates to `localhost`.

## Environment variables

| Variable | Description |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Token from @BotFather |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | GitHub OAuth App credentials |
| `GITHUB_CALLBACK_URL` | Callback URL registered on the OAuth App |
| `POSTGRES_CONNECTION_STRING` | Postgres connection string |
| `DATA_PROTECTION_KEY_PATH` | Path where encryption keys persist across deploys |
| `WEBHOOK_SECRET_TOKEN` | Validates that webhook calls actually come from Telegram |

## Bot commands

| Command | What it does |
|---|---|
| `/start` | Begins onboarding, or shows status if already connected |
| `/repo` | Change which repository receives commits |
| `/pausar` | Stop committing without disconnecting |
| `/desconectar` | Revoke GitHub access and delete the stored token |

## Roadmap

- [ ] Daily digest option (batch commits instead of one per message)
- [ ] Support for multiple repos per user
- [ ] Web landing page with live preview image
- [ ] Retry queue for failed commits (GitHub rate limits, downtime)
- [ ] Generated `README.md` index of entries, grouped by tag

## License

MIT
