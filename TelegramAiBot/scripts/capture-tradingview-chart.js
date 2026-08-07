const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright-core");

const args = parseArgs(process.argv.slice(2));
const symbol = args.symbol || "OANDA:XAUUSD";
const interval = args.interval || "60";
const output = args.output || path.join(process.cwd(), "tradingview-chart.png");

async function main() {
  const url = buildTradingViewUrl(symbol, interval);
  const browser = await chromium.launch({
    executablePath: resolveChromiumPath(),
    headless: true,
    args: [
      "--no-sandbox",
      "--disable-dev-shm-usage",
      "--disable-gpu",
      "--disable-blink-features=AutomationControlled"
    ]
  });

  try {
    const page = await browser.newPage({
      viewport: { width: 1440, height: 900 },
      locale: "vi-VN",
      timezoneId: "Asia/Bangkok",
      userAgent:
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    });

    await page.goto(url, { waitUntil: "domcontentloaded", timeout: 30000 });
    await dismissCommonOverlays(page);
    await page.locator("canvas").first().waitFor({ state: "visible", timeout: 25000 });
    await page.waitForTimeout(7000);
    await dismissCommonOverlays(page);

    fs.mkdirSync(path.dirname(output), { recursive: true });
    await page.screenshot({ path: output, fullPage: false });
    console.log(JSON.stringify({ output, url }));
  } finally {
    await browser.close();
  }
}

function parseArgs(items) {
  const parsed = {};

  for (let index = 0; index < items.length; index += 1) {
    const item = items[index];
    if (!item.startsWith("--")) {
      continue;
    }

    parsed[item.slice(2)] = items[index + 1];
    index += 1;
  }

  return parsed;
}

function buildTradingViewUrl(chartSymbol, chartInterval) {
  const params = new URLSearchParams({
    symbol: chartSymbol,
    interval: chartInterval,
    hide_side_toolbar: "1",
    hide_top_toolbar: "0"
  });

  return `https://www.tradingview.com/chart/?${params.toString()}`;
}

function resolveChromiumPath() {
  const candidates = [
    process.env.CHROMIUM_PATH,
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
    "/usr/bin/google-chrome",
    "/usr/bin/google-chrome-stable",
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"
  ].filter(Boolean);

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  throw new Error("Khong tim thay Chromium/Chrome. Hay cai chromium hoac set CHROMIUM_PATH.");
}

async function dismissCommonOverlays(page) {
  const patterns = [
    /accept/i,
    /agree/i,
    /got it/i,
    /đồng ý/i,
    /chấp nhận/i,
    /tôi hiểu/i
  ];

  for (const pattern of patterns) {
    const button = page.getByRole("button", { name: pattern }).first();
    try {
      if (await button.isVisible({ timeout: 1000 })) {
        await button.click({ timeout: 1000 });
      }
    } catch {
      // Optional overlays vary by region and are safe to ignore.
    }
  }
}

main().catch((error) => {
  console.error(error.stack || error.message || String(error));
  process.exit(1);
});
