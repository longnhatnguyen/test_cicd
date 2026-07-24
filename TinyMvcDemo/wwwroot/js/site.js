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

const assignmentList = document.getElementById("assignment-list");
const resultTitle = document.getElementById("result-title");
const resultSummary = document.getElementById("result-summary");
const resultFrame = document.getElementById("result-frame");
const openResult = document.getElementById("open-result");
const downloadResult = document.getElementById("download-result");

if (assignmentList && resultTitle && resultSummary && resultFrame && openResult && downloadResult) {
  assignmentList.addEventListener("click", event => {
    const card = event.target.closest(".assignment-card");
    if (!card) {
      return;
    }

    assignmentList.querySelectorAll(".assignment-card").forEach(item => {
      item.classList.remove("is-active");
    });
    card.classList.add("is-active");

    resultTitle.textContent = card.dataset.title;
    resultSummary.textContent = card.dataset.summary;
    resultFrame.src = card.dataset.preview;
    openResult.href = card.dataset.preview;
    downloadResult.href = card.dataset.download;
  });
}
