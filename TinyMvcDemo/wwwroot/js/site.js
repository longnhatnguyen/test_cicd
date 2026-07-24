const liveClock = document.getElementById("clock-live");
const dateNode = document.getElementById("clock-date");

if (liveClock && dateNode) {
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
    liveClock.textContent = timeFormatter.format(now);
    dateNode.textContent = dateFormatter.format(now);
  };

  renderClock();
  window.setInterval(renderClock, 1000);
}
