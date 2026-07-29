# Telegram AI Bot

Bot Telegram don gian chay bang polling, hop de deploy tren VPS Linux.

## Bien moi truong

- `TELEGRAM_BOT_TOKEN`: token tu `@BotFather`
- `OPENAI_API_KEY`: API key de goi AI
- `OPENAI_MODEL`: tuy chon, mac dinh `gpt-4.1-mini`
- `OPENAI_SYSTEM_PROMPT`: tuy chon
- `OPENAI_API_BASE`: tuy chon, mac dinh `https://api.openai.com/v1`

## Chay local hoac tren VPS

```bash
export TELEGRAM_BOT_TOKEN="<telegram-token>"
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
