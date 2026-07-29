# Telegram AI Bot

Bot Telegram don gian chay bang polling, hop de deploy tren VPS Linux.

## Bien moi truong

- `TELEGRAM_BOT_TOKEN`: token tu `@BotFather`
- `TELEGRAM_ACCESS_PASSWORD`: mat khau can nhap truoc khi dung bot
- `POSTGRES_CONNECTION_STRING`: chuoi ket noi PostgreSQL de luu session va lich su chat
- `OPENAI_API_KEY`: API key de goi AI
- `OPENAI_MODEL`: tuy chon, mac dinh `gpt-4.1-mini`
- `OPENAI_SYSTEM_PROMPT`: tuy chon
- `OPENAI_API_BASE`: tuy chon, mac dinh `https://api.openai.com/v1`

## Chay local hoac tren VPS

```bash
export TELEGRAM_BOT_TOKEN="<telegram-token>"
export TELEGRAM_ACCESS_PASSWORD="<mat-khau-vao-bot>"
export POSTGRES_CONNECTION_STRING="Host=host.docker.internal;Port=5432;Database=telegram_bot_db;Username=botuser;Password=<db-password>"
export OPENAI_API_KEY="<openai-api-key>"
export OPENAI_MODEL="gpt-4.1-mini"

dotnet run --project TelegramAiBot
```

## Publish de chay nen tren Linux

```bash
dotnet publish TelegramAiBot -c Release -o ./out/telegram-bot
cd out/telegram-bot
./TelegramAiBot
```

Luc dau nen dung polling cho de test. Khi bot chay on dinh, ban co the doi qua `systemd` hoac Docker de chay 24/7.

## Luong dang nhap

- User gui `/start`
- Bot yeu cau nhap mat khau
- Chi sau khi nhap dung `TELEGRAM_ACCESS_PASSWORD` thi bot moi goi AI
- Dung `/logout` de khoa lai phien chat

## Memory va PostgreSQL

- Bot tu tao 2 bang `telegram_chat_sessions` va `telegram_messages` neu chua co
- Trang thai da dang nhap va lich su chat duoc luu trong PostgreSQL
- Mac dinh bot nap lai `24` message gan nhat va giu toi da `30` message moi chat
- Co the doi bang bien moi truong `MAX_CONVERSATION_MESSAGES` va `STORED_MESSAGE_LIMIT`
