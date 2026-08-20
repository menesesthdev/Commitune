# Commitune

> Transforme o que você aprende em um log de TILs no GitHub. Sem cadastro. Sem dashboard. Só uma conversa com um bot.

Commitune é um bot do Telegram que transforma cada mensagem que você manda em uma entrada **TIL** (Today I Learned) commitada no seu próprio repositório do GitHub. Não existe cadastro em site nem formulário de login — você conecta sua conta do GitHub dentro da própria conversa do Telegram, escolhe o nome do repositório e começa a escrever. Cada mensagem vira um arquivo Markdown datado e com tags, e um commit cujo assunto é o que você aprendeu.

## Como funciona

1. **Abra o bot** — no Telegram (link direto na landing page) e mande `/start`.
2. **Conecte o GitHub** — toque no botão, autorize o Commitune na tela de consentimento do próprio GitHub (um toque, revogável a qualquer momento).
3. **Dê nome ao repositório** — diga ao bot como quer chamá-lo. O Commitune cria para você — **sempre como repositório privado**.
4. **Escreva o que aprendeu** — a primeira linha vira o título, o resto vira o conteúdo, e qualquer `#tag` vira tag.
5. **Veja acumular** — um log pesquisável de tudo que você aprendeu, e um gráfico de contribuições que enche conforme você escreve.

Sem dashboard. Sem senha. O bot *é* o produto.

## Como é uma entrada

Uma mensagem assim:

```
Índice não entra quando a coluna tem função
No Postgres, WHERE lower(email) = ... ignora o índice de email.
Precisa de índice na expressão. #postgres #indices
```

vira `til/2026-08-18-indice-nao-entra-quando-a-coluna-tem-funcao.md`:

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

commitada como `TIL: Índice não entra quando a coluna tem função`.

Nada na convenção é obrigatório: uma mensagem de uma linha, sem tags, é uma entrada perfeitamente válida. Duas entradas sobre o mesmo assunto no mesmo dia ganham arquivos próprios (`-2`, `-3`, …) — uma entrada nunca é escrita por cima de outra.

## Por que só repositório privado

O Commitune nunca cria um repositório público, independentemente do que o usuário pedir. Um log de aprendizado começa como anotação pessoal, e anotação é privada por padrão e por decisão — esta é uma regra dura, aplicada na chamada da API, não um default de interface que possa ser pulado em silêncio. O enunciado exato da regra está no [`CLAUDE.md`](./CLAUDE.md).

A mesma regra vale para um repositório que o Commitune não criou: `/repo <nome>` aponta para um que você já tem, mas só se ele for privado. Um público é recusado, pelo nome, com o motivo.

Se você quiser que o log seja um portfólio público, mude a visibilidade do repositório você mesmo, no GitHub. Essa é uma decisão com consequências — ela publica tudo que já foi escrito —, então ela fica com quem escreveu as entradas.

## Arquitetura

```
Telegram ──(webhook)──▶ Commitune.Api ──▶ PostgreSQL (estado do usuário)
                              │
                              ▼
                       API REST do GitHub
                  (Contents API — cria/atualiza arquivo)
```

- **API**: ASP.NET Core Minimal API (.NET 10)
- **Integração com o Telegram**: `Telegram.Bot`
- **Integração com o GitHub**: `Octokit.net`
- **Banco**: PostgreSQL + EF Core
- **Cifra do token**: API de Data Protection do ASP.NET Core
- **Landing page**: HTML e CSS estáticos no `wwwroot` da API, sem build step e sem segundo host
- **Infra**: Docker Compose numa única instância EC2, Nginx como proxy reverso, Let's Encrypt via Certbot

As mensagens são processadas de forma síncrona — sem fila no MVP. O raciocínio está no [`CLAUDE.md`](./CLAUDE.md).

## Estrutura do projeto

```
commitune/
├── src/
│   ├── Commitune.Api/             # Minimal API — webhook, callback do OAuth, endpoints
│   │   └── wwwroot/               # Landing page (HTML/CSS estáticos)
│   ├── Commitune.Domain/          # Entidades, objetos de valor, máquina de estados do onboarding
│   ├── Commitune.Infrastructure/  # Repositório Postgres, cliente do GitHub, cliente do Telegram
│   └── Commitune.Tests/           # xUnit
├── docker-compose.yml
├── CLAUDE.md
└── README.md
```

## Rodando localmente

O Telegram só entrega webhooks para uma URL HTTPS pública, então o desenvolvimento local precisa de um túnel. A ordem abaixo importa: a URL do túnel é o que tanto o Telegram quanto o GitHub são configurados contra.

```bash
# 1. Clone
git clone https://github.com/menesesthdev/Commitune.git
cd commitune

# 2. Abra um túnel e anote a URL https que ele imprime
ngrok http 5000

# 3. Configure as variáveis de ambiente
cp .env.example .env
# PUBLIC_BASE_URL   a URL https do ngrok
# GITHUB_CALLBACK_URL  $PUBLIC_BASE_URL/oauth/github/callback
# TELEGRAM_BOT_TOKEN   do @BotFather
# WEBHOOK_SECRET_TOKEN openssl rand -hex 32
# GITHUB_CLIENT_ID / GITHUB_CLIENT_SECRET  do OAuth App

# 4. Suba
docker compose up --build

# 5. Aponte o Telegram para o túnel e publique o menu de comandos
scripts/webhook.sh set
scripts/webhook.sh commands
```

Depois, mande `/start` para o bot.

Use um **bot separado para desenvolvimento**: um bot tem exatamente um webhook, então apontar o
mesmo token para dois ambientes significa que quem rodou `setWebhook` por último rouba todas as
mensagens, em silêncio. O mesmo vale para o OAuth App, que tem exatamente uma URL de callback. O
deploy está em [`docs/deploy.md`](./docs/deploy.md).

**Antes do passo 3** você precisa de duas coisas registradas:

- **Um bot**, no [@BotFather](https://t.me/botfather) — `/newbot` te dá o `TELEGRAM_BOT_TOKEN`.
- **Um OAuth App do GitHub** ([Settings → Developer settings → OAuth Apps](https://github.com/settings/developers)) — a *Authorization callback URL* dele precisa bater com `GITHUB_CALLBACK_URL` caractere por caractere, ou o GitHub recusa o redirect.

`scripts/webhook.sh info` é a primeira coisa a checar quando nada chega: ele diz para qual URL o Telegram está postando e por que a última entrega falhou. Um `401` ali significa que o segredo do `.env` e o que o Telegram guarda se separaram — rode `scripts/webhook.sh set` de novo. Uma sessão gratuita do ngrok ganha um hostname novo a cada reinício, então os passos 3 e 5 (e a callback URL do OAuth App) precisam ser refeitos toda vez.

## Variáveis de ambiente

| Variável | Descrição |
|---|---|
| `PUBLIC_BASE_URL` | URL HTTPS pública desta aplicação (túnel em dev, domínio em produção) |
| `TELEGRAM_BOT_TOKEN` | Token do @BotFather |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | Credenciais do OAuth App do GitHub |
| `GITHUB_CALLBACK_URL` | URL de callback registrada no OAuth App |
| `POSTGRES_CONNECTION_STRING` | String de conexão do Postgres |
| `DATA_PROTECTION_KEY_PATH` | Caminho onde as chaves de cifra sobrevivem aos deploys |
| `WEBHOOK_SECRET_TOKEN` | Valida que as chamadas do webhook vêm mesmo do Telegram |

## Comandos do bot

| Comando | O que faz |
|---|---|
| `/start` | Começa o onboarding, ou mostra a situação se você já está conectado |
| `/repo` | Mostra qual repositório recebe os commits |
| `/repo <nome>` | Passa a commitar naquele repositório — criando, ou usando um que você já tem (se for privado) |
| `/pausar` | Para de commitar sem desconectar |
| `/desconectar` | Revoga o acesso ao GitHub e apaga o token guardado |

## Roadmap

- [ ] Digest diário (commits em lote em vez de um por mensagem)
- [ ] Suporte a vários repositórios por usuário
- [ ] Imagem de preview de uma entrada real na landing page
- [ ] Fila de retry para commits que falharam (rate limit do GitHub, indisponibilidade)
- [ ] `README.md` gerado com o índice das entradas, agrupadas por tag

## Licença

MIT
