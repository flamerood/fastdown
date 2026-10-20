import { DEFAULTS, fileName, mediaKind, httpUrl, mergeMedia, youtubeUrl, overlayRules, mediaFilter } from "./shared.js";
const $ = id => document.getElementById(id);
const send = message => chrome.runtime.sendMessage(message);
let entries = [], filter = "all", tab;
let mediaFilterConfig = mediaFilter.defaults;
function renderFilterSettings() {
  $("media-filter-enabled").checked = mediaFilterConfig.enabled;
  $("media-filter-kb").value = mediaFilterConfig.minKB;
  $("media-filter-seconds").value = mediaFilterConfig.minSeconds;
  $("media-filter-edge").value = mediaFilterConfig.minImageEdge;
}
for (const id of ["media-filter-enabled", "media-filter-kb", "media-filter-seconds", "media-filter-edge"]) $(id).addEventListener("change", async () => {
  mediaFilterConfig = mediaFilter.normalize({ enabled: $("media-filter-enabled").checked, minKB: $("media-filter-kb").value, minSeconds: $("media-filter-seconds").value, minImageEdge: $("media-filter-edge").value });
  try { await chrome.storage.local.set({ mediaFilter: mediaFilterConfig }); renderFilterSettings(); render(); } catch (error) { feedback(error.message, true); }
});
$("show-filtered").addEventListener("change", render);
$("page-format").addEventListener("change", () => { $("page-quality").disabled = ["mp3", "m4a"].includes($("page-format").value); });
let savedRules = [], ruleBusy = false;
function renderOverlayRules() {
  const page = overlayRules.rule("page", tab?.url), site = overlayRules.rule("site", tab?.url);
  for (const [scope, item] of [["page", page], ["site", site]]) {
    $("hide-" + scope).disabled = !item || ruleBusy;
    $("hide-" + scope).checked = Boolean(item && savedRules.some(value => value.scope === scope && value.url === item.url));
    $("overlay-" + scope + "-address").textContent = item?.url || "HTTP/HTTPS sayfasında kullanılabilir";
    $("overlay-" + scope + "-address").title = item?.url || "";
  }
  $("overlay-rule-count").textContent = `(${savedRules.length})`;
  $("overlay-rule-list").replaceChildren();
  if (!savedRules.length) {
    const empty = document.createElement("p"); empty.className = "quality-help"; empty.textContent = "Henüz gizlenen sayfa veya site yok."; $("overlay-rule-list").append(empty);
  }
  for (const rule of savedRules) {
    const row = document.createElement("div"); row.className = "rule-row";
    const label = document.createElement("span"); label.textContent = (rule.scope === "site" ? "Site · " : "Sayfa · ") + rule.url; label.title = rule.url;
    const remove = document.createElement("button"); remove.type = "button"; remove.className = "quiet"; remove.textContent = "Kaldır"; remove.disabled = ruleBusy;
    remove.setAttribute("aria-label", "Gizlemeyi kaldır: " + rule.url);
    remove.addEventListener("click", () => saveOverlayRule(rule, false)); row.append(label, remove); $("overlay-rule-list").append(row);
  }
}
async function loadOverlayRules() { savedRules = overlayRules.normalize((await chrome.storage.local.get(DEFAULTS)).overlayRules); renderOverlayRules(); }
async function saveOverlayRule(rule, blocked) {
  if (!rule || ruleBusy) return;
  ruleBusy = true;
  for (const id of ["hide-page", "hide-site"]) $(id).disabled = true;
  for (const button of $("overlay-rule-list").querySelectorAll("button")) button.disabled = true;
  try {
    const reply = await send({ type: "overlay-rule", scope: rule.scope, url: rule.url, blocked });
    if (!reply?.ok) throw new Error(reply?.error || "Sayfa ayarı kaydedilemedi.");
    await loadOverlayRules();
    feedback(blocked ? "Bu " + (rule.scope === "site" ? "sitede" : "sayfada") + " medya düğmeleri kalıcı olarak gizlendi." : "Gizleme ayarı kaldırıldı.");
  } catch (error) { feedback(error.message, true); }
  finally { ruleBusy = false; renderOverlayRules(); }
}
for (const scope of ["page", "site"]) $("hide-" + scope).addEventListener("change", event => saveOverlayRule(overlayRules.rule(scope, tab?.url), event.target.checked));
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.overlayRules) { savedRules = overlayRules.normalize(changes.overlayRules.newValue); renderOverlayRules(); }
  if (area === "local" && changes.mediaFilter) { mediaFilterConfig = mediaFilter.normalize(changes.mediaFilter.newValue); renderFilterSettings(); render(); }
});
let videoUrl = null, videoLoaded = false;
async function inspectPage(url) {
  videoUrl = youtubeUrl(url) || httpUrl(url); videoLoaded = false;
  if (!videoUrl) { feedback("Geçerli bir video bağlantısı ekle.", true); return; }
  $("page-video").hidden = false; $("video-options").hidden = true;
  $("inspect-video").disabled = true; $("inspect-video").textContent = "Kaliteler alınıyor…";
  try {
    const result = await send({ type: "inspect-video", url: videoUrl });
    if (!result?.ok || !result.video) throw new Error(result?.error || "Video bilgileri alınamadı.");
    $("video-title").textContent = result.video.title;
    $("page-quality").replaceChildren(new Option("En iyi kalite + ses", "0"), ...result.video.heights.map(height => new Option(height + "p + ses", String(height))));
    $("video-options").hidden = false; videoLoaded = true;
    feedback("Kaliteyi seçip Videoyu indir'e bas.");
  } catch (error) { feedback(error.message, true); }
  finally { $("inspect-video").disabled = false; $("inspect-video").textContent = "Kaliteleri getir"; }
}
async function transferVideo() {
  if (!videoLoaded) return;
  $("download-video").disabled = true;
  feedback("Video Fastdown'a ekleniyor…");
  try {
    const reply = await send({ type: "video-download", url: videoUrl, videoHeight: ["mp3", "m4a"].includes($("page-format").value) ? 0 : Number($("page-quality").value), videoFormat: $("page-format").value });
    if (!reply?.ok) throw new Error(reply?.error || "Video aktarılamadı.");
    feedback("✓ Fastdown'a eklendi: " + reply.fileName);
  } catch (error) { feedback(error.message, true); }
  finally { $("download-video").disabled = false; }
}
function feedback(text, error = false) { $("feedback").textContent = text; $("feedback").classList.toggle("error", error); }
async function connection() {
  try {
    const result = await send({ type: "ping" });
    if (!result?.ok) throw new Error(result?.error || "Köprüye ulaşılamadı.");
    $("connection").textContent = result.appRunning ? "● Fastdown açık ve bağlı" : "● Köprü hazır · İndirme ekleyince Fastdown açılır";
    $("connection").classList.remove("error");
  } catch (error) { $("connection").textContent = "○ Köprü bağlı değil · Kurulum adımları aşağıda"; $("connection").classList.add("error"); }
}
async function transfer(entry, button) {
  button.disabled = true;
  try {
    const result = await send({ type: "download", url: entry.url, mime: entry.mime, pageUrl: entry.pageUrl || tab?.url });
    if (!result?.ok) throw new Error(result?.error || "Aktarılamadı.");
    feedback("✓ Fastdown'a eklendi: " + result.fileName);
    button.textContent = "✓";
  } catch (error) { feedback(error.message, true); }
  finally { button.disabled = false; }
}
function render() {
  $("media").replaceChildren();
  const videos = entries.filter(entry => entry.kind === "video");
  $("quality-controls").hidden = !videos.length || !["all", "video"].includes(filter);
  const selected = $("quality").value;
  const qualities = [...new Map(videos.map(entry => [entry.quality.height || "unknown", entry.quality.label])).entries()].sort((a, b) => (Number(b[0]) || 0) - (Number(a[0]) || 0));
  $("quality").replaceChildren(new Option("Tüm kaliteler", "all"), ...qualities.map(([value, label]) => new Option(label, value)));
  $("quality").value = qualities.some(([value]) => String(value) === selected) ? selected : "all";
  const chosen = $("quality").value;
  const filteredCount = entries.filter(entry => mediaFilter.reason(entry, mediaFilterConfig)).length;
  $("filtered-count").textContent = filteredCount ? `${filteredCount} medya filtrelendi · İndirmek için göster` : "Filtrelenen medya yok";
  const visible = entries.filter(entry => ($("show-filtered").checked || !mediaFilter.reason(entry, mediaFilterConfig)) && (filter === "all" || entry.kind === filter) && (chosen === "all" || !["all", "video"].includes(filter) || (entry.kind === "video" && String(entry.quality.height || "unknown") === chosen)));
  if (!visible.length) { const text = document.createElement("p"); text.className = "empty"; text.textContent = "Doğrudan medya bulunamadı. Videoyu oynatıp Yenile'ye basabilirsin."; $("media").append(text); return; }
  for (const entry of visible) {
    const row = document.createElement("div"); row.className = "media-row";
    const icon = document.createElement("span"); icon.className = "kind"; icon.textContent = { video: "▶", image: "IMG", audio: "♪" }[entry.kind] || "↓";
    const meta = document.createElement("div"); meta.className = "meta";
    const name = document.createElement("div"); name.className = "name"; name.textContent = fileName(entry.url, entry.mime); name.title = name.textContent;
    if (entry.kind === "video") {
      const quality = document.createElement("div"); quality.className = "quality"; quality.title = entry.quality.detail;
      quality.textContent = entry.quality.label + (entry.quality.source === "url" ? " · Bağlantı etiketi" : "") + (entry.current ? " · Oynatılan" : "");
      meta.append(quality);
    }
    const source = document.createElement("div"); source.className = "source"; source.textContent = new URL(entry.url).hostname + (entry.size > 0 ? ` · ${(entry.size / 1048576).toFixed(1)} MB` : "");
    meta.append(name, source);
    const reason = mediaFilter.reason(entry, mediaFilterConfig);
    if (reason) { const badge = document.createElement("small"); badge.className = "filter-reason"; badge.textContent = reason + " · İndirilebilir"; meta.append(badge); }
    const button = document.createElement("button"); button.textContent = "İndir ↓"; button.title = entry.url; button.addEventListener("click", () => transfer(entry, button));
    if(entry.stream){button.disabled=true;button.textContent=entry.stream.toUpperCase();button.title="Doğrudan aktarım yok; üstteki video sayfası çözümlemeyi deneyebilirsin.";}
    row.append(icon, meta, button); $("media").append(row);
  }
}
async function refresh() {
  try {
    [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    await loadOverlayRules();
    if (!tab?.id) { entries = []; render(); return; }
    videoUrl = youtubeUrl(tab.url) || httpUrl(tab.url); videoLoaded = false;
    $("page-video").hidden = !videoUrl; $("video-options").hidden = true;
    $("video-title").textContent = youtubeUrl(tab.url) ? "YouTube videosunu indir" : "Bu sayfadaki videoyu çözümle";
    $("refresh").disabled=true; $("refresh").textContent="Kaynaklar taranıyor…";
    const network = await send({ type: "refresh-media", tabId: tab.id });
    let dom = [];
    try { dom = (await chrome.tabs.sendMessage(tab.id, { type: "akis-dom-media" }, { frameId: 0 }))?.media || []; } catch { }
    const all = [...(network?.media || []), ...dom];
    entries = mergeMedia(all.filter(entry => httpUrl(entry.url) && (mediaKind(entry.url, entry.mime) || ["image", "video", "audio"].includes(entry.kind))).map(entry => ({ ...entry, kind: mediaKind(entry.url, entry.mime) || entry.kind })));
    if (youtubeUrl(tab.url)) entries = entries.filter(entry => entry.kind === "image");
    render();
  } catch (error) { feedback(error.message, true); }
  finally{$("refresh").disabled=false;$("refresh").textContent="↻ Yenile";}
}
for (const name of ["capture", "overlays"]) {
  $(name).addEventListener("change", () => chrome.storage.local.set({ [name]: $(name).checked }));
}
chrome.storage.local.get(DEFAULTS).then(config => { $("capture").checked = config.capture; $("overlays").checked = config.overlays; mediaFilterConfig = mediaFilter.normalize(config.mediaFilter); renderFilterSettings(); render(); });
$("filters").addEventListener("click", event => { if (!event.target.dataset.kind) return; filter = event.target.dataset.kind; for (const button of $("filters").children) button.classList.toggle("selected", button === event.target); render(); });
$("quality").addEventListener("change", () => { filter = "video"; for (const button of $("filters").children) button.classList.toggle("selected", button.dataset.kind === "video"); render(); });
$("refresh").addEventListener("click", refresh); $("retry").addEventListener("click", connection);
$("download").addEventListener("click", () => youtubeUrl($("url").value.trim()) ? inspectPage($("url").value.trim()) : transfer({ url: $("url").value.trim() }, $("download")));
$("inspect-video").addEventListener("click", () => inspectPage(videoUrl || tab?.url));
$("download-video").addEventListener("click", transferVideo);
$("url").addEventListener("keydown", event => { if (event.key === "Enter") $("download").click(); });
chrome.storage.local.get("lastTransfer").then(({ lastTransfer }) => { if (lastTransfer && Date.now() - lastTransfer.time < 60000) feedback(lastTransfer.ok ? "Son aktarılan: " + lastTransfer.name : "Son aktarma başarısız: " + lastTransfer.error, !lastTransfer.ok); });
connection(); refresh();
