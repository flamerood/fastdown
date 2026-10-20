(() => {
  const defaults = Object.freeze({ enabled: true, minKB: 64, minSeconds: 5, minImageEdge: 160 });
  function normalize(value) {
    const bounded = (v, fallback, max) => Number.isFinite(Number(v)) && Number(v) >= 0 ? Math.min(max, Number(v)) : fallback;
    return { enabled: value?.enabled !== false, minKB: bounded(value?.minKB, 64, 102400), minSeconds: bounded(value?.minSeconds, 5, 3600), minImageEdge: bounded(value?.minImageEdge, 160, 4096) };
  }
  function reason(entry, settings) {
    const config = normalize(settings);
    if (!config.enabled) return "";
    if (entry.size > 0 && entry.size < config.minKB * 1024) return "Küçük dosya";
    if (["video", "audio"].includes(entry.kind) && entry.duration > 0 && entry.duration < config.minSeconds) return "Kısa medya";
    if (entry.kind === "image" && entry.width > 0 && entry.height > 0 && Math.min(entry.width, entry.height) < config.minImageEdge) return "Küçük resim";
    return "";
  }
  globalThis.FastdownMediaFilter = Object.freeze({ defaults, normalize, reason });
})();
