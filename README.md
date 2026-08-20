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

The same rule holds for a repository Commitune did not create: `/repo <name>` will point at one you already have, but only if it is private. A public one is refused, by name, with the reason.

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
- **Landing page**: static HTML/CSS in the API's `wwwroot`, no build step and no second host
- **Infra**: Docker Compose on a single EC2 instance, Nginx reverse proxy, Let's Encrypt via Certbot

Messages are processed synchronously — no message queue in the MVP. See [`CLAUDE.md`](./CLAUDE.md) for the reasoning.

## Project structure

```
commitune/
├── src/
│   ├── Commitune.Api/             # Minimal API — webhook, OAuth callback, endpoints
│   │   └── wwwroot/               # Landing page (static HTML/CSS)
│   ├── Commitune.Domain/          # Entities, value objects, onboarding state machine
│   ├── Commitune.Infrastructure/  # Postgres repository, GitHub client, Telegram client
│   └── Commitune.Tests/           # xUnit
├── docker-compose.yml
├── CLAUDE.md
└── README.md
```

## Running locally

Telegram only delivers webhooks to a public HTTPS URL, so local development needs a tunnel. The order below matters: the tunnel URL is what both Telegram and GitHub are configured against.

```bash
# 1. Clone
git clone https://github.com/menesesthdev/Commitune.git
cd commitune

# 2. Open a tunnel and note the https URL it prints
ngrok http 5000

# 3. Configure environment variables
cp .env.example .env
# PUBLIC_BASE_URL   the https URL from ngrok
# GITHUB_CALLBACK_URL  $PUBLIC_BASE_URL/oauth/github/callback
# TELEGRAM_BOT_TOKEN   from @BotFather
# WEBHOOK_SECRET_TOKEN openssl rand -hex 32
# GITHUB_CLIENT_ID / GITHUB_CLIENT_SECRET  from the OAuth App

# 4. Run
docker compose up --build

# 5. Point Telegram at the tunnel, and publish the command menu
scripts/webhook.sh set
scripts/webhook.sh commands
```

Then send `/start` to the bot.

Use a **separate bot for development**: a bot has exactly one webhook, so pointing the same
token at two environments means whichever ran `setWebhook` last quietly takes every message.
The same goes for the OAuth App, which has exactly one callback URL. Deployment is in
[`docs/deploy.md`](./docs/deploy.md).

**Before step 3** you need two things registered:

- **A bot**, from [@BotFather](https://t.me/botfather) — `/newbot` gives you `TELEGRAM_BOT_TOKEN`.
- **A GitHub OAuth App** ([Settings → Developer settings → OAuth Apps](https://github.com/settings/developers)) — its *Authorization callback URL* must match `GITHUB_CALLBACK_URL` character for character, or GitHub refuses the redirect.

`scripts/webhook.sh info` is the first thing to check when nothing arrives: it reports what URL Telegram is posting to and why the last delivery failed. A `401` there means the secret in `.env` and the one Telegram holds have drifted apart — run `scripts/webhook.sh set` again. A free ngrok session gets a new hostname every restart, so steps 3 and 5 (and the OAuth App's callback URL) have to be redone each time.

## Environment variables

| Variable | Description |
|---|---|
| `PUBLIC_BASE_URL` | Public HTTPS base URL of this app (tunnel in dev, domain in prod) |
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
| `/repo` | Shows which repository receives commits |
| `/repo <name>` | Points commits at that repository — creating it, or using it if you already have it (and it's private) |
| `/pausar` | Stop committing without disconnecting |
| `/desconectar` | Revoke GitHub access and delete the stored token |

## Roadmap

- [ ] Daily digest option (batch commits instead of one per message)
- [ ] Support for multiple repos per user
- [ ] Preview image of a real entry on the landing page
- [ ] Retry queue for failed commits (GitHub rate limits, downtime)
- [ ] Generated `README.md` index of entries, grouped by tag

## License

MIT
