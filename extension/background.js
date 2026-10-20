import { HOST, DEFAULTS, FRAGMENT, httpUrl, fileName, mediaKind, safeReferrer, eligibleDownload, mergeMedia, videoQuality, youtubeUrl, overlayRules } from "./shared.js";
import { probeVideo, boundedResponse } from "./video-probe.js";
const sourceParser = globalThis.FastdownSources;
const probeCache = new Map(), manifestCache = new Map();
const videoCache = new Map();
async function inspectVideo(value) {
  const url = youtubeUrl(value) || httpUrl(value);
  if (!url) throw new Error("Geçerli bir video sayfası bağlantısı ekle.");
  const old = videoCache.get(url);
  if (old && Date.now() - old.time < 120000) return old.reply;
  const reply = await native({ action: "inspect-video", url });
  videoCache.set(url, { time: Date.now(), reply });
  while (videoCache.size > 20) videoCache.delete(videoCache.keys().next().value);
  return reply;
}
async function videoDownload(data) {
  const url = youtubeUrl(data.url) || httpUrl(data.url), height = Number(data.videoHeight || 0);
  const videoFormat = data.videoFormat || "mkv";
  if (!["mp4", "mkv", "mp3", "m4a"].includes(videoFormat)) throw new Error("Geçersiz indirme biçimi.");
  if (!url || !Number.isInteger(height) || (height !== 0 && (height < 72 || height > 8640))) throw new Error("Video bağlantısı veya kalite geçersiz.");
  const reply = await native({ action: "video-download", id: data.id || crypto.randomUUID(), url, videoHeight: ["mp3", "m4a"].includes(videoFormat) ? 0 : height, videoFormat, focus: true });
  await chrome.storage.local.set({ lastTransfer: { time: Date.now(), name: reply.fileName, ok: true } });
  return reply;
}

const recentMethods = new Map();
const transferring = new Set();
const tabLocks = new Map();
const settings = () => chrome.storage.local.get(DEFAULTS);
let ruleUpdates = Promise.resolve();
function setOverlayRule(message) {
  const item = overlayRules.rule(message.scope, message.url);
  if (!item || typeof message.blocked !== "boolean") return Promise.reject(new Error("Geçerli bir sayfa veya site seç."));
  const update = ruleUpdates.catch(() => {}).then(async () => {
    const rules = overlayRules.normalize((await settings()).overlayRules);
    const rest = rules.filter(value => value.scope !== item.scope || value.url !== item.url);
    if (message.blocked) {
      if (rest.length >= overlayRules.limit) throw new Error("En fazla 200 sayfa/site gizlenebilir. Önce listeden birini kaldır.");
      rest.push(item);
    }
    await chrome.storage.local.set({ overlayRules: rest });
    return { ok: true };
  });
  ruleUpdates = update;
  return update;
}
const fail = error => ({ ok: false, error: String(error?.message || error || "İşlem tamamlanamadı.") });
const native = message => new Promise((resolve, reject) => chrome.runtime.sendNativeMessage(HOST, message, reply => {
  if (chrome.runtime.lastError) reject(new Error("Fastdown köprüsüne ulaşılamadı. Eklenti-Kur.cmd dosyasını çalıştırıp tekrar dene."));
  else if (!reply?.ok) reject(new Error(reply?.error || "Fastdown indirmeyi kabul etmedi."));
  else resolve(reply);
}));

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => chrome.contextMenus.create({ id: "akis-download", title: "Fastdown ile indir", contexts: ["image", "video", "audio", "link"], documentUrlPatterns: ["http://*/*", "https://*/*"] }));
});

chrome.webRequest.onBeforeRequest.addListener(details => {
  const url = httpUrl(details.url);
  if (!url) return;
  recentMethods.delete(url);
  recentMethods.set(url, { method: details.method, time: Date.now() });
  if (recentMethods.size > 1000) recentMethods.delete(recentMethods.keys().next().value);
}, { urls: ["http://*/*", "https://*/*"], types: ["main_frame", "sub_frame", "other"] });

function rememberMedia(tabId, entry) {
  if (tabId < 0) return Promise.resolve();
  const previous = tabLocks.get(tabId) || Promise.resolve();
  const next = previous.catch(() => {}).then(async () => {
    const key = `media:${tabId}`;
    const stored = (await chrome.storage.session.get(key))[key] || [];
    const cutoff = Date.now() - 30 * 60 * 1000;
    const current = stored.filter(e => e.frameId === entry.frameId && e.time > cutoff);
    const entries = [...mergeMedia([...current, { ...entry, time: Date.now() }]), ...stored.filter(e => e.frameId !== entry.frameId && e.time > cutoff)].sort((a,b) => b.time - a.time);
    await chrome.storage.session.set({ [key]: entries.slice(0, 80) });
  });
  tabLocks.set(tabId, next);
  next.finally(() => { if (tabLocks.get(tabId) === next) tabLocks.delete(tabId); }).catch(() => {});
  return next;
}
chrome.webRequest.onHeadersReceived.addListener(details => {
  if (details.tabId < 0 || details.method !== "GET" || details.statusCode < 200 || details.statusCode >= 400) return;
  const header = name => details.responseHeaders?.find(h => h.name.toLowerCase() === name)?.value || "";
  const mime = header("content-type").split(";")[0];
  const stream = sourceParser.streamKind(details.url, mime);
  const kind = stream ? "video" : mediaKind(details.url, mime);
  if (!kind) return;
  rememberMedia(details.tabId, { url: details.url, mime, kind, stream, frameId: details.frameId, size: stream ? 0 : Number(header("content-length")) || 0 }).catch(() => {});
}, { urls: ["http://*/*", "https://*/*"], types: ["media", "image", "xmlhttprequest", "other"] }, ["responseHeaders"]);

chrome.tabs.onUpdated.addListener((id, changes) => {
  if (changes.url) chrome.tabs.sendMessage(id, { type: "akis-page-context", pageUrl: httpUrl(changes.url) }).catch(() => {});
  if (changes.status === "loading" || changes.url) {
    const pending = tabLocks.get(id) || Promise.resolve();
    const next = pending.catch(() => {}).then(() => chrome.storage.session.remove(`media:${id}`));
    tabLocks.set(id, next);
    next.finally(() => { if (tabLocks.get(id) === next) tabLocks.delete(id); }).catch(() => {});
  }
});
chrome.tabs.onRemoved.addListener(id => chrome.storage.session.remove(`media:${id}`).catch(() => {}));

async function mediaFor(tabId, frameId, analyze = false) {
  await (tabLocks.get(tabId) || Promise.resolve()).catch(() => {});
  const entries = (await chrome.storage.session.get(`media:${tabId}`))[`media:${tabId}`] || [];
  let result = entries.filter(e => !e.expanded && Date.now() - e.time < 30 * 60 * 1000 && (frameId == null || e.frameId === frameId));
  if (!analyze) return result;
  for (const entry of result.filter(e => e.stream && e.discovery !== "manifest").slice(0, 3)) {
    let cached = manifestCache.get(entry.url);
    if (!cached || Date.now() - cached.time > 120000) {
      const abort = new AbortController(), timeout = setTimeout(() => abort.abort(), 3500);
      try {
        const response = await fetch(entry.url, { credentials: "omit", signal: abort.signal });
        if (response.ok) {
          const bytes = await boundedResponse(response, sourceParser.limit, abort.signal), text = new TextDecoder().decode(bytes);
          cached = { time: Date.now(), entries: entry.stream === "hls" ? sourceParser.hls(text, response.url || entry.url) : sourceParser.dash(text, response.url || entry.url) };
          manifestCache.set(entry.url, cached); while (manifestCache.size > 80) manifestCache.delete(manifestCache.keys().next().value);
        }
      } catch { } finally { clearTimeout(timeout); }
    }
    for (const variant of cached?.entries || []) await rememberMedia(tabId, { ...variant, frameId: entry.frameId });
    if (cached?.entries?.length) await rememberMedia(tabId, { ...entry, expanded: true });
  }
  if (result.some(entry => entry.stream)) result = await mediaFor(tabId, frameId);
  const candidates = result.filter(entry => entry.kind === "video" && !entry.stream && videoQuality(entry).source === "unknown").slice(0, 6);
  async function analyzeOne(entry) {
    let cached = probeCache.get(entry.url);
    if (!cached || Date.now() - cached.time > 120000) {
      cached = { time: Date.now(), dimensions: await probeVideo(entry.url) }; probeCache.set(entry.url, cached);
      while (probeCache.size > 100) probeCache.delete(probeCache.keys().next().value);
    }
    if (cached.dimensions) await rememberMedia(tabId, { ...entry, ...cached.dimensions, measured: true, discovery: "file" });
  }
  for (let i = 0; i < candidates.length; i += 3) await Promise.allSettled(candidates.slice(i, i + 3).map(analyzeOne));
  return mediaFor(tabId, frameId);
}
async function download(data, page, focus = true) {
  const url = httpUrl(data.url);
  if (!url) throw new Error("Bu kaynak doğrudan HTTP/HTTPS dosyası değil. Blob ve korumalı yayınlar aktarılamıyor.");
  if (youtubeUrl(url)) return videoDownload({ ...data, url });
  if (FRAGMENT.test(url)) throw new Error("Bu bağlantı bir yayın listesi/parçası. Eklenti panelindeki video sayfası çözümlemeyi dene.");
  const reply = await native({ action: "download", id: data.id || crypto.randomUUID(), url, fileName: data.fileName ? fileName(url, data.mime, data.fileName) : undefined, referrer: safeReferrer(page, url), focus });
  await chrome.storage.local.set({ lastTransfer: { time: Date.now(), name: reply.fileName, ok: true } });
  return reply;
}

chrome.downloads.onCreated.addListener(item => { intercept(item).catch(() => {}); });
async function intercept(item) {
  const url = item.finalUrl || item.url;
  const methodInfo = recentMethods.get(url) || recentMethods.get(item.url);
  if (!methodInfo || Date.now() - methodInfo.time > 60000 || !eligibleDownload(item, await settings(), methodInfo.method) || transferring.has(item.id)) return;
  transferring.add(item.id);
  let paused = false;
  try {
    // A browser download remains available until the desktop has probed AND persisted it.
    await chrome.downloads.pause(item.id);
    paused = true;
    await download({ url, mime: item.mime, fileName: item.filename, id: crypto.randomUUID() }, item.referrer, false);
    await chrome.downloads.cancel(item.id);
    paused = false;
    await chrome.downloads.erase({ id: item.id }).catch(() => {});
  } catch (error) {
    if (paused) await chrome.downloads.resume(item.id).catch(() => {});
    await chrome.storage.local.set({ lastTransfer: { time: Date.now(), ok: false, name: fileName(url, item.mime, item.filename), error: error.message } });
  } finally { transferring.delete(item.id); }
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== "akis-download") return;
  download({ url: info.srcUrl || info.linkUrl }, info.frameUrl || info.pageUrl || tab?.url).then(() => {
    if (tab?.id != null) chrome.tabs.sendMessage(tab.id, { type: "akis-toast", text: "Fastdown'a eklendi." }, { frameId: info.frameId || 0 }).catch(() => {});
  }).catch(error => {
    chrome.storage.local.set({ lastTransfer: { time: Date.now(), ok: false, error: error.message } });
    if (tab?.id != null) chrome.tabs.sendMessage(tab.id, { type: "akis-toast", text: error.message }, { frameId: info.frameId || 0 }).catch(() => {});
  });
});

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id) return;
  const fromPopup = sender.url?.startsWith(chrome.runtime.getURL(""));
  const fromPage = sender.tab?.id != null && httpUrl(sender.url);
  if (!fromPopup && !fromPage) return;
  const run = async () => {
    if (message.type === "settings") return { ok: true, settings: await settings(), pageUrl: fromPage ? httpUrl(sender.tab.url) || httpUrl(sender.url) : undefined, media: fromPage ? await mediaFor(sender.tab.id, sender.frameId) : [] };
    if (message.type === "overlay-rule" && fromPopup) return setOverlayRule(message);
    if (message.type === "ping" && fromPopup) return native({ action: "ping" });
    if (message.type === "inspect-video") return inspectVideo(fromPage ? sender.url : message.url);
    if (message.type === "video-download") return videoDownload({ ...message, url: fromPage ? sender.url : message.url });
    if (message.type === "media") {
      const tabId = fromPage ? sender.tab.id : Number(message.tabId);
      return { ok: true, media: await mediaFor(tabId, fromPage ? sender.frameId : null, message.analyze === true) };
    }
    if (message.type === "refresh-media" && fromPopup) {
      const tabId = Number(message.tabId), frames = new Set([0, ...(await mediaFor(tabId)).map(entry => entry.frameId)]);
      await Promise.allSettled([...frames].slice(0,16).map(frameId => chrome.tabs.sendMessage(tabId, {type:"akis-refresh-media"}, {frameId})));
      return {ok:true, media:await mediaFor(tabId, null, true)};
    }
    if (message.type === "media-seen" && fromPage) {
      for (const entry of (Array.isArray(message.entries) ? message.entries : []).slice(0, 80)) {
        const url = httpUrl(entry.url); if (!url || entry.kind !== "video") continue;
        const stream = ["hls", "dash"].includes(entry.stream) ? entry.stream : sourceParser.streamKind(url, entry.mime);
        if (!stream && FRAGMENT.test(url)) continue;
        const width = Number(entry.width), height = Number(entry.height), measured = Boolean(entry.measured && width > 0 && height > 0 && width <= 16384 && height <= 16384);
        await rememberMedia(sender.tab.id, { url, kind: "video", stream, protected: entry.protected === true, representationId: String(entry.representationId || "").slice(0,80), qualityHint: String(entry.qualityHint || "").slice(0,80), mime: String(entry.mime || "").slice(0,100), size: Math.max(0, Number(entry.size) || 0), duration: Number.isFinite(Number(entry.duration)) ? Math.max(0, Math.min(604800, Number(entry.duration))) : 0, measured, ...(measured ? {width,height} : {}), ...(Object.hasOwn(entry,"current") ? {current:entry.current===true} : {}), discovery: String(entry.discovery || "sources").slice(0,40), frameId: sender.frameId, pageUrl: sender.url });
      }
      return { ok: true };
    }
    if (message.type === "download") return download(message, fromPage ? sender.url : message.pageUrl);
    throw new Error("Bilinmeyen eklenti işlemi.");
  };
  run().then(respond, error => respond(fail(error)));
  return true;
});
