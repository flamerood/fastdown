(() => {
  if (globalThis.FastdownMedia) return;
  const heights = new Set([144, 240, 360, 480, 540, 576, 720, 1080, 1440, 2160, 4320]);
  function parse(value, numeric = true) {
    const text = String(value || "").toLowerCase().trim();
    const dimensions = text.match(/(?:^|[^\d])(\d{3,5})\s*[x×]\s*(\d{3,5})(?!\d)/);
    if (dimensions) {
      const width = Number(dimensions[1]), height = Number(dimensions[2]), short = Math.min(width, height);
      return { height: short, width, actualHeight: height, label: heights.has(short) ? `${short}p` : `${width}×${height}` };
    }
    const p = text.match(/(?:^|[^\d])(144|240|360|480|540|576|720|1080|1440|2160|4320)p(?![a-z\d])/);
    if (p) return { height: Number(p[1]), label: p[1] + "p" };
    if (numeric && heights.has(Number(text))) return { height: Number(text), label: Number(text) + "p" };
    return null;
  }
  function quality(entry) {
    if (entry.kind !== "video") return { height: 0, label: "", source: "unknown", detail: "" };
    if (entry.measured && entry.width > 0 && entry.height > 0) {
      const result = parse(`${entry.width}x${entry.height}`);
      if (result) return { ...result, source: "metadata", detail: `${entry.width}×${entry.height} · Dosyanın gerçek çözünürlüğü` };
    }
    const hint = parse(entry.qualityHint);
    if (hint) return { ...hint, source: "label", detail: "Sitenin bu dosya için verdiği kalite etiketi" };
    try {
      const url = new URL(entry.url);
      for (const name of ["height", "quality", "resolution", "res", "quality_label", "video_quality"]) {
        const result = parse(url.searchParams.get(name));
        if (result) return { ...result, source: "url", detail: "Bağlantıdaki kalite etiketi; dosyanın çözünürlüğü doğrulanmadı" };
      }
      const path = decodeURIComponent(url.pathname);
      const result = parse(path, false);
      if (result) return { ...result, source: "url", detail: "Dosya adındaki kalite etiketi; dosyanın çözünürlüğü doğrulanmadı" };
    } catch { }
    return { height: 0, label: "Kalite bilinmiyor", source: "unknown", detail: "Bu dosya için çözünürlük bilgisi bulunamadı" };
  }
  function merge(entries) {
    const byUrl = new Map(), rank = { metadata: 3, label: 2, url: 1, unknown: 0 };
    for (const entry of entries) {
      if (!entry?.url) continue;
      const key = entry.stream === "dash" ? `${entry.url}::${entry.representationId || ""}` : entry.url;
      const existing = byUrl.get(key);
      if (!existing) { byUrl.set(key, { ...entry }); continue; }
      const better = rank[quality(entry).source] >= rank[quality(existing).source];
      const merged = { ...existing, ...entry, mime: entry.mime || existing.mime || "", size: entry.size || existing.size, duration: entry.duration || existing.duration, current: Object.hasOwn(entry, "current") ? Boolean(entry.current) : Boolean(existing.current) };
      for (const key of ["qualityHint", "measured", "width", "height"]) merged[key] = better ? (entry[key] ?? existing[key]) : (existing[key] ?? entry[key]);
      byUrl.set(key, merged);
    }
    return [...byUrl.values()].map(entry => ({ ...entry, quality: quality(entry) })).sort((a, b) => {
      if (a.kind !== b.kind) return ({ video: 0, audio: 1, image: 2 }[a.kind] ?? 3) - ({ video: 0, audio: 1, image: 2 }[b.kind] ?? 3);
      return b.quality.height - a.quality.height || Number(b.current) - Number(a.current);
    });
  }
  function format(entry) {
    const mime = entry.mime?.split(";")[0].split("/")[1];
    if (mime && /^[a-z0-9]{2,10}$/i.test(mime)) return mime.toUpperCase();
    try { return new URL(entry.url).pathname.match(/\.([a-z0-9]{2,5})$/i)?.[1].toUpperCase() || "Video"; } catch { return "Video"; }
  }
  globalThis.FastdownMedia = Object.freeze({ quality, merge, format });
})();
