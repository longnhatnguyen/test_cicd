const liveClock = document.getElementById("clock-live");
const summaryClock = document.getElementById("clock-time");
const dateNode = document.getElementById("clock-date");
const uptimeNode = document.getElementById("clock-uptime");

if (liveClock && summaryClock && dateNode && uptimeNode) {
  const startedAt = Date.now();
  const timeFormatter = new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false
  });
  const dateFormatter = new Intl.DateTimeFormat("vi-VN", {
    weekday: "long",
    day: "2-digit",
    month: "2-digit",
    year: "numeric"
  });

  const renderClock = () => {
    const now = new Date();
    const elapsedSeconds = Math.floor((Date.now() - startedAt) / 1000);
    const hours = String(Math.floor(elapsedSeconds / 3600)).padStart(2, "0");
    const minutes = String(Math.floor((elapsedSeconds % 3600) / 60)).padStart(2, "0");
    const seconds = String(elapsedSeconds % 60).padStart(2, "0");
    const timeText = timeFormatter.format(now);

    liveClock.textContent = timeText;
    summaryClock.textContent = timeText;
    dateNode.textContent = dateFormatter.format(now);
    uptimeNode.textContent = `${hours}:${minutes}:${seconds} dang online`;
  };

  renderClock();
  window.setInterval(renderClock, 1000);
}
