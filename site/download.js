// Points the download buttons at the newest release (betas included) and shows its version and size.
// Without JavaScript or when GitHub is unreachable, the buttons keep linking to the releases page.
(async () => {
  const buttons = document.querySelectorAll("[data-asset]");
  const meta = document.querySelectorAll("[data-release]");
  try {
    const response = await fetch("https://api.github.com/repos/zegolka-dev/AimOdometer/releases?per_page=10");
    if (!response.ok) return;
    const release = (await response.json()).find((r) => !r.draft);
    if (!release) return;
    for (const button of buttons) {
      const asset = release.assets.find((a) => a.name === button.dataset.asset);
      if (asset) button.href = asset.browser_download_url;
    }

    const setup = release.assets.find((a) => a.name === "AimOdometerApp-win-Setup.exe");
    const size = setup ? Math.round(setup.size / 1048576) : null;
    for (const element of meta) {
      element.textContent = element.dataset.release
        .replace("{version}", release.tag_name.replace(/^v/, ""))
        .replace("{size}", size ?? "?");
    }
  } catch {
    // Offline or rate limited: the fallback links still work.
  }
})();

// Counts clicks on the download buttons for the author: which button, the page language and the site the visitor
// came from. No cookies, nothing stored in the browser; the server keeps no IP address (see privacy.html).
document.addEventListener("click", (event) => {
  const button = event.target.closest("[data-asset][data-place]");
  if (!button) return;
  try {
    const body = JSON.stringify({
      asset: button.dataset.asset.endsWith(".zip") ? "portable" : "setup",
      language: document.documentElement.lang === "ru" ? "ru" : "en",
      place: button.dataset.place,
      referrer: document.referrer && new URL(document.referrer).host !== location.host ? document.referrer : "",
    });
    const url = "https://tphrgryvyxgldozymqzk.supabase.co/functions/v1/download-click";
    if (!navigator.sendBeacon?.(url, new Blob([body], { type: "text/plain" }))) {
      fetch(url, { method: "POST", body, keepalive: true, mode: "no-cors", headers: { "Content-Type": "text/plain" } });
    }
  } catch {
    // Counting must never get in the way of the download.
  }
});
