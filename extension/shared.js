import "./media-quality.js";
import "./source-parser.js";
import "./video-page.js";
import "./overlay-rules.js";
import "./media-filter.js";
export const mediaFilter = globalThis.FastdownMediaFilter;
export const overlayRules = globalThis.FastdownOverlayRules;
export const { youtubeUrl } = globalThis.FastdownVideoPage;
export const { quality: videoQuality, merge: mergeMedia } = globalThis.FastdownMedia;
export const HOST = "com.akis.download_manager";
export const DEFAULTS = { capture: true, overlays: true, minSizeKB: 0, overlayRules: [], mediaFilter: mediaFilter.defaults };
export const FRAGMENT = /\.(?:m3u8|mpd|m4s|ts)(?:$|[?#])/i;
export function httpUrl(value) {
  try { const url = new URL(value); return ["https:", "http:"].includes(url.protocol) && !url.username && !url.password && url.href.length <= 16384 ? url.href : null; }
  catch { return null; }
}
export function cleanName(value) {
  return String(value || "").replace(/\\/g, "/").split("/").pop().replace(/[<>:"|?*\x00-\x1f]/g, "_").slice(0, 180);
}
export function fileName(url, mime = "", suggested = "") {
  if (suggested) return cleanName(suggested);
  let name;
  try { name = cleanName(decodeURIComponent(new URL(url).pathname.split("/").pop())); } catch { name = ""; }
  if (!name || !/\.[a-z0-9]{2,6}$/i.test(name)) {
    const extension = { "image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp", "image/gif": ".gif", "image/avif": ".avif", "video/mp4": ".mp4", "video/webm": ".webm", "audio/mpeg": ".mp3", "audio/mp4": ".m4a" }[mime.split(";")[0]];
    if (extension) name = (name || "medya") + extension;
  }
  return name || "indirme";
}
export function mediaKind(url, mime = "") {
  if (!httpUrl(url) || FRAGMENT.test(url) || /mpegurl|dash\+xml/i.test(mime)) return null;
  if (/^video\//i.test(mime) || /\.(mp4|webm|mkv|mov|m4v|avi)(?:$|[?#])/i.test(url)) return "video";
  if (/^audio\//i.test(mime) || /\.(mp3|m4a|wav|ogg|flac)(?:$|[?#])/i.test(url)) return "audio";
  if (/^image\//i.test(mime) || /\.(jpe?g|png|gif|webp|avif|svg)(?:$|[?#])/i.test(url)) return "image";
  return null;
}
export function safeReferrer(page, target) {
  const source = httpUrl(page), destination = httpUrl(target);
  if (!source || !destination) return undefined;
  const from = new URL(source), to = new URL(destination);
  if (from.protocol === "https:" && to.protocol === "http:") return undefined;
  from.hash = "";
  return from.origin === to.origin ? from.href : from.origin + "/";
}
export function eligibleDownload(item, settings, method) {
  return Boolean(settings.capture && method === "GET" && item.state === "in_progress" && !item.byExtensionId && !item.incognito &&
    (!item.danger || item.danger === "safe") && httpUrl(item.finalUrl || item.url) && !FRAGMENT.test(item.finalUrl || item.url) &&
    !/text\/html|application\/xhtml\+xml/i.test(item.mime || "") && (!(item.totalBytes > 0) || item.totalBytes >= settings.minSizeKB * 1024));
}
