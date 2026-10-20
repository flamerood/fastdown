(() => {
  if (globalThis.FastdownSources) return;
  const LIMIT = 1024 * 1024;
  const http = (value, base) => { try { const url = new URL(value, base); return /^https?:$/.test(url.protocol) && !url.username && !url.password && url.href.length <= 16384 ? url.href : null; } catch { return null; } };
  function streamKind(url, type = "") { return /\.m3u8(?:$|[?#])|mpegurl/i.test(url + " " + type) || /^hls$/i.test(type) ? "hls" : /\.mpd(?:$|[?#])|dash\+xml/i.test(url + " " + type) || /^dash$/i.test(type) ? "dash" : null; }
  function extract(data, base) {
    const found = new Map(); let visited = 0;
    function add(value, object = {}, key = "") {
      if (typeof value !== "string" || value.length > 16384) return;
      if (key && !/^(src|file|url|uri|path|hls|dash|video|video_url|videoUrl|contentUrl|downloadUrl|playbackUrl|manifest|source|original)$/i.test(key) && !/^(144|240|360|480|540|576|720|1080|1440|2160|4320)p?$/.test(key)) return;
      const url = http(value, base), type = String(object.mimeType || object.mime || object.type || (/^(hls|dash)$/i.test(key)?key:""));
      if (!url || (!/\.(mp4|webm|mkv|mov|m4v|m3u8|mpd)(?:$|[?#])/i.test(url) && !/video|mpegurl|dash\+xml|^mp4$|^webm$|^hls$|^dash$/i.test(type))) return;
      const height = Number(object.height || object.resolution?.height), width = Number(object.width || object.resolution?.width);
      const label = object.qualityLabel || object.quality_label || object.label || object.quality || object.res || object.resolution || (/^(144|240|360|480|540|576|720|1080|1440|2160|4320)p?$/.test(key) ? key : "");
      found.set(url, { url, kind: "video", mime: /^video\/|mpegurl|dash\+xml/i.test(type) ? type.slice(0, 100) : "", qualityHint: height > 0 && height < 16384 ? `${width > 0 ? width + "x" : ""}${height}` : (typeof label === "string" || typeof label === "number" ? String(label).slice(0, 80) : ""), size: Math.max(0, Number(object.size || object.filesize || object.fileSize) || 0), stream: streamKind(url, type), discovery: "sources" });
    }
    function walk(value, depth = 0) {
      if (depth > 10 || ++visited > 10000 || found.size >= 80) return;
      if (typeof value === "string") { add(value); return; }
      if (!value || typeof value !== "object") return;
      if (Array.isArray(value)) { value.slice(0, 500).forEach(item => walk(item, depth + 1)); return; }
      for (const [key, item] of Object.entries(value).slice(0, 500)) {
        if (typeof item === "string") add(item, value, key);
        else if (item && typeof item === "object") walk(item, depth + 1);
      }
    }
    walk(data); return [...found.values()];
  }
  function parseText(text, base) {
    if (typeof text !== "string" || text.length > LIMIT) return [];
    try { return extract(JSON.parse(text), base); } catch { }
    const entries = [];
    // Read ordinary source-object literals without evaluating any page code.
    const normalized = text.replace(/\\\//g, "/").replace(/\\u002f/gi, "/");
    for (const match of normalized.matchAll(/\{[^{}]{1,20000}\}/g)) {
      const fragment = match[0], object = {};
      for (const property of fragment.matchAll(/["']?(src|file|url|type|mime|label|quality|qualityLabel|height|width|size)["']?\s*:\s*(?:["']([^"']*)["']|(\d+))/g)) object[property[1]] = property[2] ?? Number(property[3]);
      entries.push(...extract(object, base)); if (entries.length >= 80) break;
    }
    return [...new Map(entries.map(entry => [entry.url, entry])).values()];
  }
  function attributes(text) {
    return Object.fromEntries([...text.matchAll(/([A-Z0-9-]+)=("[^"]*"|[^,]*)/gi)].map(match => [match[1].toUpperCase(), match[2].replace(/^"|"$/g, "")]));
  }
  function hls(text, base) {
    if (!text.trimStart().startsWith("#EXTM3U") || text.length > LIMIT) return [];
    let pending = null;
    const entries = [], protectedStream = /#EXT-X-(?:SESSION-)?KEY:(?![^\r\n]*METHOD=NONE)/i.test(text);
    for (const line of text.split(/\r?\n/)) {
      if (line.startsWith("#EXT-X-STREAM-INF:")) { pending = attributes(line.slice(line.indexOf(":") + 1)); continue; }
      if (pending && line.trim() && !line.startsWith("#")) {
        const url = http(line.trim(), base), dimensions = pending.RESOLUTION || "";
        if (url) entries.push({ url, kind: "video", stream: "hls", protected: protectedStream, qualityHint: dimensions, bandwidth: Number(pending.BANDWIDTH) || 0, discovery: "manifest", mime: "application/vnd.apple.mpegurl" });
        pending = null;
      }
    }
    return entries.slice(0, 80);
  }
  function dash(text, base) {
    if (text.length > LIMIT || !/<(?:\w+:)?MPD[\s>]/i.test(text) || /<!DOCTYPE|<!ENTITY/i.test(text)) return [];
    const root = { tag: "", attrs: {}, children: [], text: "" }, stack = [root]; let count = 0;
    const decode = value => value.replace(/&amp;/g, "&").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, "<").replace(/&gt;/g, ">");
    for (const token of text.replace(/<!--[\s\S]*?-->/g, "").matchAll(/<\/?[^>]+>|[^<]+/g)) {
      const value = token[0]; if (++count > 20000) return [];
      if (value.startsWith("</")) { if (stack.length > 1) stack.pop(); }
      else if (value.startsWith("<?") || value.startsWith("<!")) continue;
      else if (value.startsWith("<")) {
        const tag = value.match(/^<([^\s/>]+)/)?.[1].split(":").pop(); if (!tag) continue;
        const attrs = Object.fromEntries([...value.matchAll(/([\w:-]+)\s*=\s*["']([^"']*)["']/g)].map(match => [match[1], decode(match[2])]));
        const node = { tag, attrs, children: [], text: "" }; stack.at(-1).children.push(node); if (!value.endsWith("/>")) { if (stack.length > 20) return []; stack.push(node); }
      } else stack.at(-1).text += decode(value);
    }
    const entries = [];
    function walk(node, parentBase = base, inherited = {}, protectedStream = false) {
      const ownBase = node.children.find(child => child.tag === "BaseURL")?.text.trim();
      const currentBase = ownBase ? http(ownBase, parentBase) || parentBase : parentBase;
      const attrs = { ...inherited, ...node.attrs };
      protectedStream ||= node.children.some(child => child.tag === "ContentProtection");
      if (node.tag === "Representation" && (attrs.contentType === "video" || /^video\//.test(attrs.mimeType || "") || attrs.height)) {
        entries.push({ url: currentBase, representationId: attrs.id || String(entries.length), kind: "video", stream: "dash", protected: protectedStream, qualityHint: attrs.width && attrs.height ? `${attrs.width}x${attrs.height}` : attrs.height || "", bandwidth: Number(attrs.bandwidth) || 0, discovery: "manifest", mime: "application/dash+xml" });
      }
      for (const child of node.children) if (child.tag !== "BaseURL" && entries.length < 80) walk(child, currentBase, attrs, protectedStream);
    }
    walk(root); return entries;
  }
  globalThis.FastdownSources = Object.freeze({ extract, parseText, hls, dash, streamKind, limit: LIMIT });
})();
