# GitHub Secrets Template

Tao cac secret sau trong repo `Settings -> Secrets and variables -> Actions`.

## Secrets can tao

```text
SERVER_HOST=
SERVER_PORT=
SERVER_USER=
SERVER_SSH_PASSWORD=
GHCR_READ_TOKEN=
TELEGRAM_BOT_TOKEN=
TELEGRAM_ACCESS_PASSWORD=
POSTGRES_CONNECTION_STRING=
OPENAI_API_KEY=
```

## Y nghia

- `SERVER_HOST`: IP hoac domain server
- `SERVER_PORT`: cong SSH, thuong la `22`
- `SERVER_USER`: user dung de deploy
- `SERVER_SSH_PASSWORD`: mat khau SSH cua user deploy
- `GHCR_READ_TOKEN`: token GitHub co quyen `read:packages`
- `TELEGRAM_BOT_TOKEN`: token bot tu `@BotFather`
- `TELEGRAM_ACCESS_PASSWORD`: mat khau nguoi dung can nhap truoc khi noi chuyen voi bot
- `POSTGRES_CONNECTION_STRING`: chuoi ket noi PostgreSQL cho bot, vi du `Host=host.docker.internal;Port=5432;Database=telegram_bot_db;Username=botuser;Password=...`
- `OPENAI_API_KEY`: API key dung de bot goi AI
- `SERVER_HOST` van la dia chi SSH cua server, khong phai domain public cua web

## Ghi chu

- `SERVER_SSH_PASSWORD` la mat khau dang dung de SSH vao server
- `GHCR_READ_TOKEN` nen la Personal Access Token co quyen `read:packages`
- `OPENAI_MODEL` khong bat buoc tao secret, workflow mac dinh dung `gpt-4.1-mini`
- Khong commit gia tri that cua secrets vao repo
