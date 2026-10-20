(() => {
  if (window.__akisSourceObserver) return;
  window.__akisSourceObserver = true;
  const parser = globalThis.FastdownSources, cache = new Map(), seenScripts = new WeakSet();
  const channel = "akis-source-discovery-v1";
  let currentPage = location.href;
  function publish(entries) {
    if (location.href !== currentPage) { cache.clear(); currentPage = location.href; }
    for (const entry of entries) { cache.delete(entry.url); cache.set(entry.url, entry); }
    while (cache.size > 80) cache.delete(cache.keys().next().value);
    if (entries.length) window.postMessage({ channel, entries: entries.slice(0, 40) }, location.origin);
  }
  async function read(response) {
    if (!response?.ok || /\.(ts|m4s|mp4|webm)(?:$|[?#])/i.test(response.url)) return;
    const type = response.headers.get("content-type") || "";
    if (!/json|mpegurl|dash\+xml/.test(type) && !/\.m3u8|\.mpd/i.test(response.url)) return;
    if (Number(response.headers.get("content-length")) > parser.limit) return;
    const copy = response.clone(), reader = copy.body?.getReader(); if (!reader) return;
    let bytes = 0, text = ""; const decoder = new TextDecoder();
    try {
      while (true) { const { value, done } = await reader.read(); if (done) break; bytes += value.length; if (bytes > parser.limit) return; text += decoder.decode(value, { stream: true }); }
      text += decoder.decode(); processText(text, response.url);
    } finally { reader.cancel().catch(() => {}); }
  }
  function processText(text, base) {
    if (/^\s*#EXTM3U/.test(text)) publish(parser.hls(text, base));
    else if (/<(?:\w+:)?MPD[\s>]/.test(text)) publish(parser.dash(text, base));
    else publish(parser.parseText(text, base));
  }
  try {
    window.fetch = new Proxy(window.fetch, { apply(target, receiver, args) {
      const promise = Reflect.apply(target, receiver, args); promise.then(response => read(response).catch(() => {}), () => {}); return promise;
    } });
    const observed = new WeakSet();
    XMLHttpRequest.prototype.open = new Proxy(XMLHttpRequest.prototype.open, { apply(target, receiver, args) {
      if (!observed.has(receiver)) {
        observed.add(receiver); receiver.addEventListener("load", () => {
          try {
            if (receiver.status < 200 || receiver.status >= 400) return;
            if (receiver.responseType === "json") publish(parser.extract(receiver.response, receiver.responseURL));
            else if (["", "text"].includes(receiver.responseType) && receiver.responseText.length <= parser.limit) processText(receiver.responseText, receiver.responseURL);
          } catch { }
        });
      }
      return Reflect.apply(target, receiver, args);
    } });
  } catch { }
  function scan() {
    for (const script of document.querySelectorAll("script:not([src])")) {
      if (seenScripts.has(script) || script.textContent.length > parser.limit) continue;
      seenScripts.add(script); processText(script.textContent, location.href);
    }
    for (const node of document.querySelectorAll("[data-setup],[data-sources],[data-config],media-player")) {
      for (const attribute of ["data-setup", "data-sources", "data-config", "src"]) { const value = node.getAttribute(attribute); if (value && value.length <= parser.limit) processText(value, location.href); }
      try { if (node.localName === "media-player") publish(parser.extract(node.src, location.href)); } catch { }
    }
    try { if (typeof window.jwplayer === "function") { const player = window.jwplayer(); if (player?.getPlaylist) publish(parser.extract(player.getPlaylist(), location.href)); } } catch { }
    try { if (window.videojs?.getPlayers) for (const player of Object.values(window.videojs.getPlayers())) { if (player?.options) publish(parser.extract(player.options()?.sources, location.href)); } } catch { }
  }
  let timer;
  function schedule() { clearTimeout(timer); timer = setTimeout(scan, 150); }
  document.addEventListener("DOMContentLoaded", () => {
    scan(); const observer = new MutationObserver(schedule); observer.observe(document.documentElement, { childList: true, subtree: true });
  }, { once: true });
  window.addEventListener("message", event => { if (event.source === window && event.data?.channel === "akis-source-request-v1") { scan(); window.postMessage({ channel, entries: [...cache.values()].slice(0, 40), requestId:event.data.requestId }, location.origin); } });
})();
