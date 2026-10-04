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
