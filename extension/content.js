(() => {
  if (globalThis.__akisInstalled) return;
  globalThis.__akisInstalled = true;
  let overlays = true, settingsReady = false, rules = [], pageUrl = location.href, dismissalPage = "";
  let dismissedElements = new WeakMap(), dismissedSources = new Set();
  let target = null, timer = null, positionFrame = null, busy = false, choosing = false, chooserTarget = null;
  let host, root, trigger, button, toast, chooser, choices, formatSelect, chooserEntries;
  const filterApi = globalThis.FastdownMediaFilter;
  let filterConfig = filterApi.defaults, networkMedia = new Map();
  function filtered(element) { return collect(element).some(entry => filterApi.reason({ ...networkMedia.get(entry.url), ...entry, size: networkMedia.get(entry.url)?.size || 0 }, filterConfig)); }
  const ruleApi = globalThis.FastdownOverlayRules;
  const { merge: mergeMedia, format } = globalThis.FastdownMedia;
  const { youtubeUrl } = globalThis.FastdownVideoPage;
  const parser = globalThis.FastdownSources, scanRequests = new Map(); let scanId = 0;
  const http = value => { if (typeof value !== "string" || !value.trim()) return null; try { const url = new URL(value, location.href); return /^https?:$/.test(url.protocol) && !url.username && !url.password ? url.href : null; } catch { return null; } };
  const fragmented = url => /\.(m3u8|mpd|m4s|ts)(?:$|[?#])/i.test(url);
  function syncPage() {
    if (window === window.top) pageUrl = location.href;
    const key = ruleApi.pageKey(pageUrl) + "|" + ruleApi.pageKey(location.href);
    if (key !== dismissalPage) { dismissalPage = key; dismissedElements = new WeakMap(); dismissedSources = new Set(); }
  }
  function allowed() { syncPage(); return settingsReady && overlays && !ruleApi.matches(rules, pageUrl, location.href); }
  function identity(element) { return element instanceof HTMLVideoElement ? "video" : (element.src || element.currentSrc || "") + "|" + (element.srcset || ""); }
  function sourceKey(element) {
    const source = element instanceof HTMLVideoElement && youtubeUrl(location.href) || element.currentSrc || element.src;
    return source && source.length <= 16384 ? element.tagName + ":" + source : null;
  }
  function dismissed(element) { return dismissedElements.get(element) === identity(element) || Boolean(sourceKey(element) && dismissedSources.has(sourceKey(element))); }
  function hideOverlay() { closeChoices(); target = null; if (trigger) trigger.style.display = "none"; }
  function dismissCurrent(event) {
    if (!event.isTrusted || !target) return;
    event.preventDefault(); event.stopPropagation(); syncPage();
    dismissedElements.set(target, identity(target));
    const source = sourceKey(target);
    if (source) { dismissedSources.add(source); if (dismissedSources.size > 500) dismissedSources.delete(dismissedSources.values().next().value); }
    hideOverlay();
  }
  const send = message => new Promise(resolve => {
    try { chrome.runtime.sendMessage(message, result => { if (chrome.runtime.lastError) resolve({ ok: false, error: "Eklenti yenilendi. Sayfayı yeniden yükle." }); else resolve(result || { ok: false }); }); }
    catch { resolve({ ok: false, error: "Eklenti yenilendi. Sayfayı yeniden yükle." }); }
  });
  const report = entries => send({type:"media-seen", entries:entries.filter(entry=>entry.kind==="video").slice(0,80)});
  window.addEventListener("message", event => {
    if (event.source !== window || event.origin !== location.origin || event.data?.channel !== "akis-source-discovery-v1" || !Array.isArray(event.data.entries)) return;
    const entries = event.data.entries.slice(0,80).filter(entry=>entry && typeof entry.url==="string" && entry.url.length<=16384 && http(entry.url)).map(entry=>({...entry,measured:false,kind:"video"}));
    report(entries).finally(()=>{ const resolve=scanRequests.get(event.data.requestId); if(resolve){scanRequests.delete(event.data.requestId);resolve();} });
  });
  function rescan() {
    const id=String(++scanId);
    return new Promise(resolve=>{ const timer=setTimeout(()=>{scanRequests.delete(id);resolve();},900);scanRequests.set(id,()=>{clearTimeout(timer);resolve();});window.postMessage({channel:"akis-source-request-v1",requestId:id},location.origin); });
  }
  function hint(element) {
    if (!element) return "";
    for (const name of ["label", "data-quality", "data-res", "res", "size", "data-height"]) {
      const value = element.getAttribute(name); if (value) return value.slice(0, 80);
    }
    return "";
  }
  function collect(element) {
    const image = element instanceof HTMLImageElement, video = element instanceof HTMLVideoElement;
    if (video && element.mediaKeys) return [];
    const kind = image ? "image" : video ? "video" : "audio";
    const current = http(element.currentSrc || element.src);
    const sources = image ? [] : [...element.querySelectorAll("source")];
    const nodes = [{ url: current, node: sources.find(node => http(node.src) === current) || element, current: true }, ...sources.map(node => ({ url: http(node.src), node, current: http(node.src) === current }))];
    if (!image && element.src && http(element.src) !== current) nodes.push({ url: http(element.src), node: element, current: false });
    return mergeMedia(nodes.filter(entry => entry.url && (!fragmented(entry.url)||parser.streamKind(entry.url))).map(entry => ({
      url: entry.url, kind, mime: image ? "" : entry.node.getAttribute("type") || "", qualityHint: hint(entry.node), current: entry.current,
      stream:parser.streamKind(entry.url,entry.node.getAttribute("type")||""),
      measured: video && entry.current && element.readyState >= 2 && element.videoWidth > 0 && element.videoHeight > 0,
      width: image ? element.naturalWidth : video && entry.current ? element.videoWidth : undefined, height: image ? element.naturalHeight : video && entry.current ? element.videoHeight : undefined,
      duration: !image && Number.isFinite(element.duration) ? element.duration : undefined
    })));
  }
  function ensureUI() {
    if (host?.isConnected) return;
    host = document.createElement("akis-download-ui");
    host.style.cssText = "all:initial!important;position:fixed!important;inset:0!important;pointer-events:none!important;z-index:2147483647!important;display:block!important;";
    root = host.attachShadow({ mode: "open" });
    const style = document.createElement("style");
    style.textContent = `:host{all:initial}.trigger{all:initial;box-sizing:border-box;position:fixed;display:none;align-items:center;gap:7px;padding:10px 13px;background:#99f0bf;color:#10251a;font:600 12px/1.3 system-ui,sans-serif;cursor:pointer;border:1px solid #4e9a6b;border-radius:8px;box-shadow:0 3px 18px #0006;pointer-events:auto;white-space:nowrap}.trigger:hover{background:#b9f8d4}button:focus-visible{outline:2px solid #e1ffe9;outline-offset:2px}button:disabled{cursor:wait;opacity:.6}.toast{position:fixed;bottom:24px;right:24px;max-width:min(410px,85vw);padding:14px 18px;border:1px solid #3d6650;border-radius:10px;background:#17241c;color:#dbf3e3;font:13px/1.5 system-ui,sans-serif;box-shadow:0 4px 24px #0005;display:none;pointer-events:auto}.chooser{display:none;position:fixed;width:330px;max-width:calc(100vw - 16px);box-sizing:border-box;background:#161f1b;border:1px solid #466451;border-radius:11px;padding:15px;color:#e6f6eb;font:12px/1.5 system-ui,sans-serif;pointer-events:auto;box-shadow:0 10px 35px #0008}.heading{display:flex;justify-content:space-between;align-items:center;font-size:14px;font-weight:650}.close{all:initial;cursor:pointer;color:#c4dccc;font:20px system-ui;padding:0 3px}.help{font-size:10px;color:#9fb4a7;margin:6px 0 13px}.choices{max-height:min(310px,45vh);overflow:auto;scrollbar-width:thin}.choice{all:initial;box-sizing:border-box;display:flex;justify-content:space-between;gap:10px;width:100%;padding:11px 10px;margin-bottom:5px;border:1px solid #304c3a;border-radius:7px;cursor:pointer;color:#dfeee4;font:12px/1.4 system-ui,sans-serif;background:#1d2d23}.choice:hover{background:#2c4735;border-color:#88c69e}.choice b{display:block;color:#b0f1c6;font-size:13px}.choice small{display:block;max-width:222px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#8ea698;font-size:10px;margin-top:4px}.arrow{align-self:center;color:#99f0bf;font-size:18px}`;
    style.textContent += `.trigger{padding:0;gap:0;overflow:hidden;cursor:default}.download,.dismiss{all:initial;box-sizing:border-box;cursor:pointer;color:#10251a;font:600 12px/1.3 system-ui,sans-serif;padding:10px 12px;pointer-events:auto}.download:hover,.dismiss:hover{background:#c4fadc}.dismiss{border-left:1px solid #67ac80;font:18px/1 system-ui;padding:9px 10px}.download:focus-visible,.dismiss:focus-visible{outline:2px solid #173d24;outline-offset:-3px}`;
    trigger = document.createElement("div"); trigger.className = "trigger";
    button = document.createElement("button"); button.type = "button"; button.className = "download"; button.textContent = "↓ Fastdown ile indir"; button.setAttribute("aria-label", "Bu medyayı Fastdown ile indir");
    const dismiss = document.createElement("button"); dismiss.type = "button"; dismiss.className = "dismiss"; dismiss.textContent = "×"; dismiss.setAttribute("aria-label", "Bu medyada bu seferlik gizle"); dismiss.title = "Sayfayı yenileyene kadar bu medyada gösterme"; dismiss.addEventListener("click", dismissCurrent);
    trigger.append(button, dismiss);
    trigger.addEventListener("pointerenter", () => clearTimeout(timer)); trigger.addEventListener("pointerleave", hideLater);
    trigger.addEventListener("focusin", () => clearTimeout(timer)); trigger.addEventListener("focusout", hideLater);
    button.addEventListener("click", async event => {
      if (!event.isTrusted || busy || !target || !allowed() || dismissed(target)) return;
      event.preventDefault(); event.stopPropagation();
      const element = target;
      busy = true; button.disabled = true; button.textContent = "Dosyalar aranıyor…";
      try {
        const variants = await sources(element);
        if (!allowed() || dismissed(element) || !element.isConnected) return;
        if (!variants.length) throw new Error("Doğrudan video dosyası bulunamadı. Eklenti panelindeki video sayfası çözümlemeyi deneyebilirsin.");
        if (element instanceof HTMLVideoElement) showChoices(variants, element);
        else await transfer(variants[0]);
      } catch (error) { showToast(error.message); }
      finally { busy = false; button.disabled = false; button.textContent = "↓ Fastdown ile indir"; hideLater(); }
    });
    chooser = document.createElement("section"); chooser.className = "chooser"; chooser.setAttribute("role", "dialog"); chooser.setAttribute("aria-label", "Video kalitesi seç");
    const heading = document.createElement("div"); heading.className = "heading"; heading.textContent = "Video ve kalite seç";
    const close = document.createElement("button"); close.className = "close"; close.type = "button"; close.textContent = "×"; close.setAttribute("aria-label", "Kalite listesini kapat"); close.addEventListener("click", closeChoices); heading.append(close);
    const help = document.createElement("p"); help.className = "help"; help.textContent = "Sunucunun kaynak listesi ve dosya bilgileri taranır. İndirmek istediğin kaliteyi seç.";
    choices = document.createElement("div"); choices.className = "choices";
    formatSelect = document.createElement("select"); formatSelect.setAttribute("aria-label", "İndirme biçimi"); formatSelect.style.cssText = "width:100%;margin:0 0 12px;padding:8px;background:#24392c;color:#d0f2da;border:1px solid #42614a;border-radius:6px";
    for (const [value, text] of [["mp4","MP4 · Video + ses"],["mkv","MKV · Video + ses"],["mp3","MP3 · Sadece ses"],["m4a","M4A · Sadece ses"]]) formatSelect.append(new Option(text, value));
    formatSelect.addEventListener("change", () => showChoices(chooserEntries, chooserTarget, true));
    chooser.append(heading, help, formatSelect, choices);
    toast = document.createElement("div"); toast.className = "toast"; toast.setAttribute("role", "status");
    root.append(style, trigger, chooser, toast); document.documentElement.append(host);
  }
  async function sources(element) {
    if (element instanceof HTMLVideoElement && element.mediaKeys) throw new Error("Bu video DRM koruması kullanıyor; aktarılamıyor.");
    if (element instanceof HTMLVideoElement && youtubeUrl(location.href)) {
      const result = await send({ type: "inspect-video" });
      if (!result.ok || !result.video) throw new Error(result.error || "Video bilgileri alınamadı.");
      return result.video.heights.map(height => ({ url: youtubeUrl(location.href), kind: "video", pageVideo: true, videoHeight: height, title: result.video.title, mime: "video/x-matroska", quality: { height, label: height + "p", source: "extractor", detail: "Video ve ses tek MKV dosyasında birleştirilir." } }));
    }
    const own = collect(element);
    if (element instanceof HTMLImageElement) return own;
    await report(own); await rescan();
    const result = await send({ type: "media", analyze:true });
    const observed = (result.media || []).filter(entry => entry.kind === "video");
    return mergeMedia([...observed, ...own]);
  }
  async function transfer(entry) {
    const result = await send({ type: entry.pageVideo ? "video-download" : "download", url: entry.url, mime: entry.mime, videoHeight: entry.videoHeight, videoFormat: entry.videoFormat });
    if (!result.ok) throw new Error(result.error || "İndirme aktarılamadı.");
    showToast("✓ Fastdown'a eklendi: " + (result.fileName || "medya"));
  }
  function closeChoices() { choosing = false; chooserTarget = null; if (chooser) chooser.style.display = "none"; hideLater(); }
  function showChoices(entries, element, keepFormat = false) {
    chooserEntries = entries;
    const pageVideo = entries.some(entry => entry.pageVideo);
    formatSelect.hidden = !pageVideo;
    if (!keepFormat) formatSelect.value = "mp4";
    const audioOnly = pageVideo && ["mp3", "m4a"].includes(formatSelect.value);
    choosing = true; chooserTarget = element; clearTimeout(timer); choices.replaceChildren();
    for (const entry of (audioOnly ? entries.slice(0, 1) : entries)) {
      const option = document.createElement("button"); option.className = "choice"; option.type = "button";
      const label = document.createElement("span"), title = document.createElement("b"), detail = document.createElement("small"), arrow = document.createElement("span");
      title.textContent = audioOnly ? `${formatSelect.value.toUpperCase()} · Sadece ses` : `${entry.quality.label} · ${entry.pageVideo ? "Video + ses · " + formatSelect.value.toUpperCase() : format(entry)}${entry.current ? " · Oynatılan" : ""}`;
      if(entry.stream) title.textContent += ` · ${entry.stream.toUpperCase()}`;
      let name; try { name = decodeURIComponent(new URL(entry.url).pathname.split("/").pop()) || new URL(entry.url).hostname; } catch { name = "Video dosyası"; }
      detail.textContent = (entry.title || name) + (entry.quality.source === "url" ? " · Bağlantı etiketi" : ""); option.title = entry.quality.detail + "\n" + entry.url;
      option.setAttribute("aria-label", audioOnly ? `${formatSelect.value.toUpperCase()} sadece ses indir` : `${entry.quality.label} indir · ${name}`);
      if(entry.stream){option.disabled=true;option.title="Bu kaynağın doğrudan aktarımı yok. Eklenti panelinde video sayfasını çözümlemeyi dene.";detail.textContent="Yayın listesi · Sayfa çözümleme ile deneyebilirsin";}
      label.append(title, detail); arrow.className = "arrow"; arrow.textContent = "↓"; option.append(label, arrow);
      option.addEventListener("click", async event => {
        if (!event.isTrusted || busy) return; busy = true;
        const buttons = [...choices.querySelectorAll("button")]; buttons.forEach(button => { button.disabled = true; });
        try { await transfer({ ...entry, ...(entry.pageVideo ? { videoFormat: formatSelect.value } : {}) }); closeChoices(); } catch (error) { showToast(error.message); }
        finally { busy = false; buttons.forEach(button => { button.disabled = false; }); }
      });
      choices.append(option);
    }
    chooser.style.display = "block"; positionChooser(); choices.querySelector("button")?.focus({ preventScroll: true });
  }
  function positionChooser() {
    if (!choosing || !chooserTarget?.isConnected) { if (choosing) closeChoices(); return; }
    const rect = chooserTarget.getBoundingClientRect();
    chooser.style.left = Math.max(8, Math.min(innerWidth - chooser.offsetWidth - 8, rect.right - chooser.offsetWidth - 8)) + "px";
    chooser.style.top = Math.max(8, Math.min(innerHeight - chooser.offsetHeight - 8, rect.top + 45)) + "px";
  }
  let toastTimer;
  function showToast(text) { ensureUI(); toast.textContent = text; toast.style.display = "block"; clearTimeout(toastTimer); toastTimer = setTimeout(() => { toast.style.display = "none"; }, 7000); }
  function position() {
    positionFrame = null; if (choosing) positionChooser();
    if (!trigger || !target?.isConnected || !allowed() || dismissed(target) || filtered(target)) { if (trigger) trigger.style.display = "none"; return; }
    const rect = target.getBoundingClientRect();
    if (rect.bottom < 24 || rect.top >= innerHeight || rect.right < 0 || rect.left >= innerWidth) { trigger.style.display = "none"; return; }
    trigger.style.display = "inline-flex";
    trigger.style.left = Math.max(4, Math.min(innerWidth - trigger.offsetWidth - 4, rect.right - trigger.offsetWidth - 8)) + "px";
    trigger.style.top = Math.max(4, Math.min(innerHeight - trigger.offsetHeight - 4, rect.top + 8)) + "px";
  }
  function schedulePosition() { if (!positionFrame) positionFrame = requestAnimationFrame(position); }
  function hideLater() { clearTimeout(timer); timer = setTimeout(() => { if (!busy && !choosing) { target = null; if (trigger) trigger.style.display = "none"; } }, 300); }
  document.addEventListener("pointerover", event => {
    if (busy || choosing || event.target === host) return;
    const element = event.composedPath().find(entry => entry instanceof HTMLVideoElement || entry instanceof HTMLImageElement);
    if (!element) { hideLater(); return; }
    if (!allowed()) { target = settingsReady ? null : element; if (trigger) trigger.style.display = "none"; return; }
    if (filtered(element)) { hideOverlay(); return; }
    if (dismissed(element)) { hideOverlay(); return; }
    const rect = element.getBoundingClientRect(); if (rect.width < 96 || rect.height < 64) return;
    clearTimeout(timer); target = element; ensureUI(); schedulePosition();
  }, true);
  document.addEventListener("pointerout", event => { if (event.target === target && event.relatedTarget !== host) hideLater(); }, true);
  document.addEventListener("pointerdown", event => { if (choosing && !event.composedPath().includes(host)) closeChoices(); }, true);
  document.addEventListener("keydown", event => { if (event.key === "Escape" && choosing) { closeChoices(); button?.focus(); } }, true);
  addEventListener("scroll", schedulePosition, true); addEventListener("resize", schedulePosition);
  addEventListener("pagehide", () => { clearTimeout(timer); clearTimeout(toastTimer); });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.overlays) overlays = changes.overlays.newValue !== false;
    if (changes.overlayRules) rules = ruleApi.normalize(changes.overlayRules.newValue);
    if (changes.mediaFilter) { filterConfig = filterApi.normalize(changes.mediaFilter.newValue); if (target && filtered(target)) hideOverlay(); }
    if (!allowed()) hideOverlay();
  });
  chrome.runtime.onMessage.addListener((message, _sender, respond) => {
    if (message.type === "akis-page-context") { pageUrl = message.pageUrl || location.href; syncPage(); if (!allowed()) hideOverlay(); respond({ ok: true }); return; }
    if (message.type === "akis-toast") { showToast(message.text); return; }
    if (message.type === "akis-dom-media") respond({ media: mergeMedia([...document.querySelectorAll("img,video,audio")].flatMap(collect)).slice(0, 80) });
    if(message.type === "akis-refresh-media"){rescan().then(()=>report([...document.querySelectorAll("video")].flatMap(collect))).then(()=>respond({ok:true}));return true;}
  });
  let reporting;
  function reportVideos(){clearTimeout(reporting);reporting=setTimeout(()=>{report([...document.querySelectorAll("video")].flatMap(collect));if(target && filtered(target))hideOverlay();},150);}
  document.addEventListener("load",e=>{if(e.target instanceof HTMLImageElement && target===e.target && filtered(target))hideOverlay();},true);
  for(const event of ["loadedmetadata","loadeddata","playing","resize"])document.addEventListener(event,e=>{if(e.target instanceof HTMLVideoElement)reportVideos();},true);
  rescan().then(reportVideos);
  send({ type: "settings" }).then(result => {
    pageUrl = result.pageUrl || location.href; overlays = result.settings?.overlays !== false; rules = ruleApi.normalize(result.settings?.overlayRules); filterConfig = filterApi.normalize(result.settings?.mediaFilter); networkMedia = new Map((result.media || []).map(entry => [entry.url, entry])); settingsReady = result.ok === true;
    if (!allowed()) hideOverlay();
    else if (target?.isConnected && target.matches(":hover")) { const rect = target.getBoundingClientRect(); if (rect.width >= 96 && rect.height >= 64) { ensureUI(); schedulePosition(); } }
  });
})();
