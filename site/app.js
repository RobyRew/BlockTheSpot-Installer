const status = document.getElementById('release-status');
try {
  const response = await fetch('https://api.github.com/repos/RobyRew/BlockTheSpot-Installer/releases/latest', {signal: AbortSignal.timeout(6000)});
  if (response.ok) {
    const release = await response.json();
    if (/^v\d+\.\d+\.\d+$/.test(release.tag_name)) {
      status.textContent = `${release.tag_name} · Windows 10 & 11 · x64 · Runtime included`;
    }
  }
} catch {
  // The stable release download works even if GitHub's metadata API is unavailable.
}
