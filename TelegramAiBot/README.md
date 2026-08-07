# Telegram AI Bot

Bot Telegram don gian chay bang polling, hop de deploy tren VPS Linux.

## Bien moi truong

- `TELEGRAM_BOT_TOKEN`: token tu `@BotFather`
- `TELEGRAM_ACCESS_PASSWORD`: mat khau can nhap truoc khi dung bot
- `OPENAI_API_KEY`: API key de goi AI
- `OPENAI_MODEL`: tuy chon, mac dinh `gpt-4.1-mini`
- `OPENAI_SYSTEM_PROMPT`: tuy chon
- `OPENAI_API_BASE`: tuy chon, mac dinh `https://api.openai.com/v1`
- `CHART_ANALYSIS_ENABLED`: bat job chup va phan tich chart dinh ky, mac dinh `false`
- `CHART_ANALYSIS_CHAT_ID`: Telegram chat id nhan anh va phan tich
- `CHART_ANALYSIS_URL`: link TradingView public, bot se boc query `symbol`
- `CHART_ANALYSIS_SYMBOL`: symbol TradingView, mac dinh `OANDA:XAUUSD`
- `CHART_ANALYSIS_INTERVAL`: timeframe cu, mac dinh `M5`
- `CHART_ANALYSIS_INTERVALS`: danh sach timeframe da khung, mac dinh `M5,M15,H1,H4`
- `CHART_ANALYSIS_PERIOD_MINUTES`: chu ky phan tich, mac dinh `5`
- `CHART_ANALYSIS_SEND_NO_TRADE`: gui ca ket qua dung ngoai, mac dinh `false`
- `CHART_CAPTURE_TIMEOUT_SECONDS`: timeout chup chart, mac dinh `90`
- `CHROMIUM_PATH`: duong dan Chromium, trong Docker mac dinh `/usr/bin/chromium`

## Chay local hoac tren VPS

```bash
export TELEGRAM_BOT_TOKEN="<telegram-token>"
export TELEGRAM_ACCESS_PASSWORD="<mat-khau-vao-bot>"
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

## Runtime state

- Bot khong luu lich su chat
- Bot chi giu trang thai da nhap password trong RAM cua container
- Khi container restart, user can nhap lai `TELEGRAM_ACCESS_PASSWORD`

## Chup va phan tich chart TradingView

Lay chat id:

```text
/chatid
```

Chup chart thu cong:

```text
/chart OANDA:XAUUSD H1
/chart https://vn.tradingview.com/chart/?symbol=OANDA%3AXAUUSD H1
```

Chup va phan tich thu cong:

```text
/chart OANDA:XAUUSD M15 analyze
```

Bat job tu dong 5 phut/lan tren server:

```bash
export CHART_ANALYSIS_ENABLED=true
export CHART_ANALYSIS_CHAT_ID="<telegram-chat-id>"
export CHART_ANALYSIS_URL="https://vn.tradingview.com/chart/?symbol=OANDA%3AXAUUSD"
export CHART_ANALYSIS_INTERVALS="M5,M15,H1,H4"
export CHART_ANALYSIS_PERIOD_MINUTES=5
export CHART_ANALYSIS_SEND_NO_TRADE=false
```

Bot dung Chromium headless trong Docker de mo TradingView public chart, chup anh tung timeframe, gui nhieu anh vao OpenAI vision model, roi tra ve vung entry/SL/TP theo kich ban xac suat.

Ket qua AI bat dau bang `SIGNAL: ENTRY` hoac `SIGNAL: NO_TRADE`. Job tu dong chi gui anh va tin hieu vao Telegram khi co `SIGNAL: ENTRY`, tru khi `CHART_ANALYSIS_SEND_NO_TRADE=true`. Neu TradingView chan IP/user-agent, bot se bao loi thay vi tu dat lenh.

### GitHub Secrets can co

Neu repo da co cac secret nen tang sau thi bot da du dieu kien deploy va goi AI:

```text
GHCR_READ_TOKEN
OPENAI_API_KEY
SERVER_HOST
SERVER_PORT
SERVER_SSH_PASSWORD
SERVER_USER
TELEGRAM_ACCESS_PASSWORD
TELEGRAM_BOT_TOKEN
```

De bat job tu dong chup va phan tich chart, them toi thieu:

```text
CHART_ANALYSIS_ENABLED=true
CHART_ANALYSIS_CHAT_ID=<telegram-chat-id>
```

Cac secret chart ben duoi khong bat buoc vi code da co gia tri mac dinh:

```text
CHART_ANALYSIS_URL=https://vn.tradingview.com/chart/?symbol=OANDA%3AXAUUSD
CHART_ANALYSIS_INTERVALS=M5,M15,H1,H4
CHART_ANALYSIS_PERIOD_MINUTES=5
CHART_ANALYSIS_SEND_NO_TRADE=false
```
