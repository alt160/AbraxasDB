function wireRailResizer() {
  const handle = document.querySelector("#rail-resizer");
  const storageKey = "abraxas-showcase-rail-width";
  const minimum = 210;
  const maximum = 480;
  const clampWidth = (width) =>
    Math.max(minimum, Math.min(maximum, window.innerWidth - 420, width));
  const setWidth = (width, persist = false) => {
    const next = Math.round(clampWidth(width));
    document.documentElement.style.setProperty("--rail-width", `${next}px`);
    handle.setAttribute("aria-valuenow", String(next));
    if (persist) {
      try { localStorage.setItem(storageKey, String(next)); } catch { /* The rail still resizes when storage is unavailable. */ }
    }
  };
  try {
    const saved = Number(localStorage.getItem(storageKey));
    if (Number.isFinite(saved) && saved >= minimum) setWidth(saved);
  } catch { /* Use the stylesheet default. */ }
  handle.onpointerdown = (event) => {
    if (event.button !== 0) return;
    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    handle.classList.add("dragging");
    document.body.classList.add("rail-resizing");
  };
  handle.onpointermove = (event) => {
    if (handle.hasPointerCapture(event.pointerId)) setWidth(event.clientX);
  };
  const finish = (event) => {
    if (!handle.hasPointerCapture(event.pointerId)) return;
    handle.releasePointerCapture(event.pointerId);
    handle.classList.remove("dragging");
    document.body.classList.remove("rail-resizing");
    setWidth(parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--rail-width")), true);
  };
  handle.onpointerup = finish;
  handle.onpointercancel = finish;
  handle.onkeydown = (event) => {
    const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--rail-width"));
    const step = event.shiftKey ? 32 : 8;
    const next = event.key === "ArrowLeft" ? current - step
      : event.key === "ArrowRight" ? current + step
      : event.key === "Home" ? minimum
      : event.key === "End" ? maximum
      : null;
    if (next === null) return;
    event.preventDefault();
    setWidth(next, true);
  };
  window.addEventListener("resize", () => {
    const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--rail-width"));
    setWidth(current);
  });
}
