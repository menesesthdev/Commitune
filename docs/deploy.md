# Deploying Commitune

Target: the existing EC2 `t4g.micro`, behind the Nginx already running there, at
`commitune.menesesthdev.com.br`. Docker Compose runs the API and Postgres; Nginx terminates TLS
and proxies to the loopback.

```
Telegram ─┐
          ├─ https://commitune.menesesthdev.com.br ─▶ Nginx ─▶ 127.0.0.1:5000 ─▶ api ─▶ db
GitHub  ──┘                                          (TLS)      (compose)
```

## Before anything

Production needs its **own** bot and its **own** OAuth App — not the ones used locally:

- **A bot has exactly one webhook.** Point the same token at two environments and whichever ran
  `setWebhook` last silently takes every message. Keep the real bot for production and use a
  second one for development.
- **An OAuth App has exactly one callback URL.** Editing the development app's URL breaks local
  work; register a separate app whose callback is
  `https://commitune.menesesthdev.com.br/oauth/github/callback`, character for character.

Check the DNS record before starting — everything below assumes it resolves to the instance:

```bash
dig +short commitune.menesesthdev.com.br
```

## First deploy

```bash
# 1. Code on the instance
git clone https://github.com/NICHOLAST0RRES/commitune.git
cd commitune

# 2. Configuration
cp .env.example .env
```

`.env` on the server, and how it differs from the local one:

| Variable | Value |
|---|---|
| `PUBLIC_BASE_URL` | `https://commitune.menesesthdev.com.br` |
| `GITHUB_CALLBACK_URL` | `https://commitune.menesesthdev.com.br/oauth/github/callback` |
| `TELEGRAM_BOT_TOKEN` | the production bot |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | the production OAuth App |
| `WEBHOOK_SECRET_TOKEN` | a fresh one: `openssl rand -hex 32` |
| `POSTGRES_PASSWORD` | a real password — the example ships `change-me` |
| `API_HOST_PORT` | `5000`, or another free port if something else on the host has it |

```bash
# 3. Certificate, before Nginx is pointed at it — the site config references the
#    certificate files, so enabling it first would keep nginx from starting.
sudo certbot certonly --nginx -d commitune.menesesthdev.com.br

# 4. Nginx site
sudo cp deploy/nginx/commitune.conf /etc/nginx/sites-available/commitune
sudo ln -s /etc/nginx/sites-available/commitune /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx

# 5. Bring it up (see the note on building below)
docker compose up -d --build
docker compose logs -f api        # expect "Applying 1 pending migration(s)"

# 6. Tell Telegram where to deliver, and what the bot can do
scripts/webhook.sh set
scripts/webhook.sh commands
```

Verify from outside the instance:

```bash
curl -fsS https://commitune.menesesthdev.com.br/health   # {"status":"ok"}
scripts/webhook.sh info                                  # url set, no last_error_message
```

Then send `/start` to the production bot.

### Building on a t4g.micro

The image builds from the SDK image, and `dotnet publish` on 1 GiB of RAM is tight — a build
killed for memory looks like a compiler crash with no error. If it happens, add swap once:

```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

Both images are `linux/arm64` on Graviton, which the official .NET images publish — no
cross-building involved.

## Updating

```bash
git pull
docker compose up -d --build
docker compose logs -f api
```

Migrations run at startup, one container, no second replica to race with (see `CLAUDE.md`).
Nginx and the webhook registration are untouched by a redeploy — re-run `scripts/webhook.sh set`
only if `PUBLIC_BASE_URL` changed.

## The volume you cannot lose

`dpkeys` holds the Data Protection key ring. **Every stored GitHub token is encrypted with it.**
Lose it and every connected user has to reconnect — the bot handles that gracefully ("reconnect"
instead of a crash), but it is still every user, at once.

```bash
# Back up both volumes
docker run --rm -v commitune_dpkeys:/keys -v "$PWD":/backup alpine \
  tar czf /backup/dpkeys-$(date +%F).tar.gz -C /keys .
docker compose exec db pg_dump -U commitune commitune | gzip > db-$(date +%F).sql.gz
```

`docker compose down -v` deletes both volumes. `down` without `-v` does not.

## When something is wrong

| Symptom | Where to look |
|---|---|
| Nothing arrives from Telegram | `scripts/webhook.sh info` — `last_error_message` says whether it is TLS, a 404 or a 401 |
| `401` in that output | The secret in `.env` and the one Telegram holds have drifted apart; run `scripts/webhook.sh set` again |
| GitHub refuses the redirect | `GITHUB_CALLBACK_URL` and the OAuth App's callback differ — they must match exactly |
| Bot replies "algo deu errado" | `docker compose logs api`; user ids and status codes are logged, never tokens or entries |
| `502` from Nginx | The API is down or on another port: `docker compose ps`, then check `API_HOST_PORT` against `proxy_pass` |

The API listens on `127.0.0.1` only, so nothing reaches it except through Nginx. It is worth
keeping that way: the webhook's secret-token check is authentication, not transport security.
