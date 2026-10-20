import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { DEFAULTS, httpUrl, mediaKind, eligibleDownload, fileName, safeReferrer, videoQuality, mergeMedia, youtubeUrl, overlayRules, mediaFilter } from "../../extension/shared.js";

const event = () => { const listeners = []; return { listeners, addListener: fn => listeners.push(fn) }; };
function store(initial = {}) {
  const data = { ...initial };
  return { data, async get(keys) { if (typeof keys === "string") return { [keys]: data[keys] }; if (Array.isArray(keys)) return Object.fromEntries(keys.map(k => [k, data[k]])); return { ...keys, ...data }; }, async set(values) { Object.assign(data, values); }, async remove(key) { delete data[key]; } };
}
let serial = 0;
async function harness({ config = {}, nativeError = false } = {}) {
  const calls = [], local = store(config), session = store();
  const chrome = {
    runtime: { id: "test-extension", onInstalled: event(), onMessage: event(), getURL: path => "chrome-extension://test-extension/" + path,
      sendNativeMessage(host, message, callback) { calls.push(["native", message]); if (nativeError) this.lastError = { message: "not installed" }; callback(nativeError ? undefined : { ok: true, fileName: "video.mp4", jobId: "job-1" }); this.lastError = undefined; } },
    storage: { local, session }, contextMenus: { onClicked: event(), removeAll: cb => cb(), create: opts => calls.push(["menu", opts]) },
    tabs: { onUpdated: event(), onRemoved: event(), async sendMessage(...args) { calls.push(["tab", ...args]); } },
    webRequest: { onBeforeRequest: event(), onHeadersReceived: event() },
    downloads: { onCreated: event(), async pause(id) { calls.push(["pause", id]); }, async resume(id) { calls.push(["resume", id]); }, async cancel(id) { calls.push(["cancel", id]); }, async erase(query) { calls.push(["erase", query]); } }
  };
  globalThis.chrome = chrome;
  await import(`../../extension/background.js?harness=${serial++}`);
  const settle = async () => { for (let n = 0; n < 10; n++) await new Promise(resolve => setImmediate(resolve)); };
  return { chrome, calls, local, session, settle,
    async capture(method = "GET", overrides = {}) {
      chrome.webRequest.onBeforeRequest.listeners[0]({ url: "https://media.example/video.mp4", method });
      chrome.downloads.onCreated.listeners[0]({ id: 7, url: "https://media.example/video.mp4", finalUrl: "https://media.example/video.mp4", filename: "C:\\Downloads\\video.mp4", state: "in_progress", mime: "video/mp4", danger: "safe", totalBytes: 12345, referrer: "https://example.com/watch?id=1", ...overrides });
      await settle();
    },
    async message(message, sender = { id: "test-extension", url: "https://example.com/watch", frameId: 0, tab: { id: 3 } }) {
      let response;
      const handled = chrome.runtime.onMessage.listeners[0](message, sender, result => { response = result; });
      if (!handled) return undefined;
      await settle();
      return response;
    }
  };
}

test("Extension key has a stable ID and MV3 least-required integration permissions", async () => {
  const manifest = JSON.parse(await readFile(new URL("../../extension/manifest.json", import.meta.url), "utf8"));
  const identity = JSON.parse(await readFile(new URL("../../extension/identity.json", import.meta.url), "utf8"));
  const id = [...createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest().subarray(0, 16)].map(b => String.fromCharCode(97 + (b >> 4), 97 + (b & 15))).join("");
  assert.equal(id, identity.id); assert.equal(manifest.manifest_version, 3);
  assert(!manifest.permissions.includes("cookies")); assert(!manifest.permissions.includes("debugger"));
});
test("Only credential-free HTTP(S) URLs are forwarded", () => {
  for (const url of ["file:///C:/secret", "javascript:alert(1)", "blob:https://example.com/uuid", "data:text/plain,x", "https://u:p@example.com/file", "invalid"]) assert.equal(httpUrl(url), null);
  assert.equal(httpUrl("https://example.com/a.zip"), "https://example.com/a.zip");
});
test("Media filters use known size/duration/intrinsic dimensions without guessing unknown metadata", () => {
  assert.equal(mediaFilter.reason({kind:"image",size:1024},mediaFilter.defaults),"Küçük dosya");
  assert.equal(mediaFilter.reason({kind:"video",duration:2},{minKB:0}),"Kısa medya");
  assert.equal(mediaFilter.reason({kind:"image",width:128,height:128},{minKB:0}),"Küçük resim");
  assert.equal(mediaFilter.reason({kind:"video",size:0,duration:0},mediaFilter.defaults),"");
  assert.equal(mediaFilter.reason({kind:"video",duration:5,size:65536},mediaFilter.defaults),"");
  assert.equal(mediaFilter.reason({kind:"image",size:1,width:1,height:1},{enabled:false}),"");
  assert.equal(mediaFilter.reason({kind:"image",size:1,width:1,height:1},{minKB:0,minImageEdge:0}),"");
});
test("Audio format handoff removes video resolution and rejects arbitrary encoder commands", async () => {
  const h=await harness(),sender={id:"test-extension",url:"chrome-extension://test-extension/popup.html"};
  await h.message({type:"video-download",url:"https://www.youtube.com/watch?v=jNQXAC9IVRw",videoHeight:1080,videoFormat:"mp3"},sender);
  const request=h.calls.find(c=>c[0]==="native")[1];assert.equal(request.videoFormat,"mp3");assert.equal(request.videoHeight,0);
  const bad=await h.message({type:"video-download",url:"https://example.com/page",videoFormat:"mp3 --exec calc"},sender);assert.equal(bad.ok,false);
  assert.equal(h.calls.filter(c=>c[0]==="native").length,1);
});

test("Hidden pages preserve video IDs, ignore anchors and normalize query order", () => {
  const rules=overlayRules.normalize([{scope:"page",url:"https://www.youtube.com/watch?z=2&v=jNQXAC9IVRw#t=4"}]);
  assert(overlayRules.matches(rules,"https://www.youtube.com/watch?v=jNQXAC9IVRw&z=2#comments"));
  assert(!overlayRules.matches(rules,"https://www.youtube.com/watch?v=another1234&z=2"));
  assert(!overlayRules.matches(rules,"https://www.youtube.com/watch?v=jNQXAC9IVRw&z=20"));
});
test("Site hiding uses exact origin and applies to a parent page or an embedded frame", () => {
  const rules=overlayRules.normalize([{scope:"site",url:"https://example.com/ignored?query=1"}]);
  assert(overlayRules.matches(rules,"https://example.com/one","https://player.test/embed"));
  assert(overlayRules.matches(rules,"https://other.test/page","https://example.com/embed"));
  for(const url of ["https://example.com.evil.test/page","https://sub.example.com/page","http://example.com/page","https://example.com:444/page"])assert(!overlayRules.matches(rules,url));
});
test("Invalid, credential-bearing and oversized hiding rules cannot enter settings", () => {
  const rules=overlayRules.normalize([{scope:"all",url:"https://example.com"},{scope:"site",url:"file:///C:/secret"},{scope:"page",url:"https://u:p@example.com/page"},{scope:"page",url:"https://example.com/"+"a".repeat(16384)},{scope:"site",url:"https://example.com/one"},{scope:"site",url:"https://example.com/two"}]);
  assert.deepEqual(rules,[{scope:"site",url:"https://example.com"}]);
});
test("Only extension UI can persist exclusions; removing one preserves the others", async () => {
  const h=await harness(),owner={id:"test-extension",url:"chrome-extension://test-extension/popup.html"};
  assert.equal((await h.message({type:"overlay-rule",scope:"site",url:"https://example.com",blocked:true})).ok,false);
  await h.message({type:"overlay-rule",scope:"page",url:"https://example.com/one",blocked:true},owner);
  await h.message({type:"overlay-rule",scope:"site",url:"https://other.test/anything",blocked:true},owner);
  await h.message({type:"overlay-rule",scope:"page",url:"https://example.com/one#fragment",blocked:false},owner);
  assert.deepEqual(h.local.data.overlayRules,[{scope:"site",url:"https://other.test"}]);
  assert.equal(h.calls.filter(c=>c[0]==="native").length,0);
});
test("Embedded media receives the browser-supplied parent URL and SPA changes broadcast it", async () => {
  const h=await harness();
  const result=await h.message({type:"settings",pageUrl:"https://fake.test"},{id:"test-extension",url:"https://frame.test/player",frameId:2,tab:{id:3,url:"https://example.com/page"}});
  assert.equal(result.pageUrl,"https://example.com/page");
  h.chrome.tabs.onUpdated.listeners[0](3,{url:"https://example.com/next"});await h.settle();
  assert(h.calls.some(c=>c[0]==="tab"&&c[1]===3&&c[2].type==="akis-page-context"&&c[2].pageUrl==="https://example.com/next"));
});

test("YouTube watch, Shorts, live, embed and short links identify one canonical video", () => {
  const expected = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
  for (const value of [expected + "&list=ignored", "https://youtu.be/jNQXAC9IVRw?t=2", "https://m.youtube.com/shorts/jNQXAC9IVRw", "https://www.youtube-nocookie.com/embed/jNQXAC9IVRw", "https://www.youtube.com/live/jNQXAC9IVRw"]) assert.equal(youtubeUrl(value), expected);
  for (const value of ["https://youtube.com.evil.test/watch?v=jNQXAC9IVRw", "https://youtube.com/playlist?list=test", "https://youtube.com/watch?v=invalid", "https://u:p@youtube.com/watch?v=jNQXAC9IVRw", "javascript:alert(1)"]) assert.equal(youtubeUrl(value),null);
});

test("YouTube links use page extraction rather than a direct HTML file download", async () => {
  const h = await harness();
  const reply = await h.message({type:"download",url:"https://youtu.be/jNQXAC9IVRw",videoHeight:240});
  assert.equal(reply.ok,true);
  const request=h.calls.find(c=>c[0]==="native")[1];
  assert.equal(request.action,"video-download");assert.equal(request.videoHeight,240);assert.equal(request.url,"https://www.youtube.com/watch?v=jNQXAC9IVRw");
});

test("Video resolution uses the content frame URL and never accepts shell options", async () => {
  const h = await harness();
  await h.message({type:"inspect-video",url:"https://evil.test/override"});
  assert.equal(h.calls.find(c=>c[0]==="native")[1].url,"https://example.com/watch");
  const result=await h.message({type:"video-download",videoHeight:"720;calc.exe"});
  assert.equal(result.ok,false);
  assert.equal(h.calls.filter(c=>c[0]==="native").length,1);
});
test("Media classifiers reject HLS, DASH and stream fragments", () => {
  assert.equal(mediaKind("https://example.com/a.mp4"), "video");
  assert.equal(mediaKind("https://example.com/image?id=1", "image/jpeg"), "image");
  for (const ext of ["m3u8", "mpd", "m4s", "ts"]) assert.equal(mediaKind(`https://example.com/a.${ext}`, "video/mp4"), null);
});
test("File names strip directories and referrers follow cross-origin downgrade rules", () => {
  assert.equal(fileName("https://x.test/a", "video/mp4", "C:\\Downloads\\clip.mp4"), "clip.mp4");
  assert.equal(fileName("https://x.test/resource", "image/jpeg"), "resource.jpg");
  assert.equal(safeReferrer("https://x.test/watch?secret=1#hash", "https://cdn.test/a.mp4"), "https://x.test/");
  assert.equal(safeReferrer("https://x.test/watch#hash", "https://x.test/a.mp4"), "https://x.test/watch");
  assert.equal(safeReferrer("https://x.test/watch", "http://x.test/a.mp4"), undefined);
});
test("Capture completes only after native acknowledgement", async () => {
  const h = await harness(); await h.capture();
  assert.deepEqual(h.calls.map(c => c[0]), ["pause", "native", "cancel", "erase"]);
  assert.equal(h.calls[1][1].fileName, "video.mp4"); assert.equal(h.calls[1][1].focus, false);
  assert.equal(h.local.data.lastTransfer.ok, true);
});
test("Missing desktop bridge resumes original download without cancelling it", async () => {
  const h = await harness({ nativeError: true }); await h.capture();
  assert.deepEqual(h.calls.map(c => c[0]), ["pause", "native", "resume"]);
  assert.equal(h.local.data.lastTransfer.ok, false);
});
test("An early browser event without a filename lets the server choose Content-Disposition", async () => {
  const h = await harness(); await h.capture("GET", { filename: "" });
  assert.equal(h.calls.find(c => c[0] === "native")[1].fileName, undefined);
});
test("POST, incognito, dangerous and other-extension downloads stay with browser", async () => {
  const h = await harness();
  await h.capture("POST"); await h.capture("GET", { incognito: true }); await h.capture("GET", { danger: "dangerous" }); await h.capture("GET", { byExtensionId: "another-extension" });
  assert.deepEqual(h.calls, []);
  assert.equal(eligibleDownload({ state: "in_progress", url: "blob:https://example.com/a" }, DEFAULTS, "GET"), false);
});
test("Automatic capture can be turned off", async () => {
  const h = await harness({ config: { capture: false } }); await h.capture(); assert.deepEqual(h.calls, []);
});
test("Duplicate download events cannot transfer a file twice", async () => {
  const h = await harness();
  h.chrome.webRequest.onBeforeRequest.listeners[0]({ url: "https://media.example/video.mp4", method: "GET" });
  const item = { id: 11, state: "in_progress", url: "https://media.example/video.mp4" };
  h.chrome.downloads.onCreated.listeners[0](item); h.chrome.downloads.onCreated.listeners[0](item);
  await h.settle(); assert.equal(h.calls.filter(c => c[0] === "native").length, 1);
});
test("Untrusted senders cannot access native messaging", async () => {
  const h = await harness();
  assert.equal(await h.message({ type: "download", url: "https://example.com/a.zip" }, { id: "hostile", url: "https://evil.test", tab: { id: 3 } }), undefined);
  assert.equal((await h.message({ type: "download", url: "file:///C:/secret" })).ok, false);
  assert.equal((await h.message({ type: "ping" })).ok, false);
  assert.equal(h.calls.length, 0);
});
test("Direct media transfer uses the sending frame's page as referrer", async () => {
  const h = await harness();
  const reply = await h.message({ type: "download", url: "https://cdn.test/a.mp4", pageUrl: "https://spoofed.test/" });
  assert.equal(reply.ok, true); assert.equal(h.calls[0][1].referrer, "https://example.com/");
});
test("Owned extension pages can check the bridge even when opened in a browser tab", async () => {
  const h = await harness();
  const reply = await h.message({ type: "ping" }, { id: "test-extension", url: "chrome-extension://test-extension/popup.html", tab: { id: 99 } });
  assert.equal(reply.ok, true); assert.equal(h.calls[0][1].action, "ping");
});
test("Network media detection is isolated per tab and frame", async () => {
  const h = await harness();
  const emit = (url, frameId) => h.chrome.webRequest.onHeadersReceived.listeners[0]({ tabId: 3, frameId, method: "GET", statusCode: 200, url, responseHeaders: [{ name: "Content-Type", value: "video/mp4" }] });
  emit("https://cdn.test/one.mp4", 0); emit("https://cdn.test/two.mp4", 4); emit("https://cdn.test/fragment.m4s", 0);
  await h.settle();
  const result = await h.message({ type: "media" });
  assert.equal(result.media.length, 1); assert.equal(result.media[0].url, "https://cdn.test/one.mp4");
  h.chrome.tabs.onRemoved.listeners[0](3); await h.settle();
  assert.equal((await h.message({ type: "media" })).media.length, 0);
});

test("Quality reads explicit 1080p and 720p site labels", () => {
  for (const height of [360, 480, 720, 1080, 1440, 2160]) {
    const quality = videoQuality({ kind: "video", url: "https://cdn.test/stream", qualityHint: `${height}p` });
    assert.equal(quality.label, `${height}p`); assert.equal(quality.source, "label");
  }
});
test("Intrinsic dimensions outrank site labels and URL hints", () => {
  const quality = videoQuality({ kind: "video", url: "https://cdn.test/video-720p.mp4", qualityHint: "480p", measured: true, width: 1920, height: 1080 });
  assert.equal(quality.label, "1080p"); assert.equal(quality.source, "metadata");
});
test("Display size alone never claims a video's quality", () => {
  assert.equal(videoQuality({ kind: "video", url: "https://cdn.test/stream", width: 1920, height: 1080 }).source, "unknown");
});
test("Path and selected query parameters expose labelled but unverified quality", () => {
  for (const url of ["https://cdn.test/movie-1080p.mp4", "https://cdn.test/stream?height=1080", "https://cdn.test/stream?quality=1080p", "https://cdn.test/movie-1920x1080.mp4"]) {
    const quality = videoQuality({ kind: "video", url }); assert.equal(quality.label, "1080p"); assert.equal(quality.source, "url");
  }
});
test("Unrelated IDs and bitrates do not fabricate 1080p", () => {
  for (const url of ["https://cdn.test/video.mp4?id=1080", "https://cdn.test/stream?bitrate=1080000", "https://cdn.test/movie-2026.mp4"]) assert.equal(videoQuality({ kind: "video", url }).label, "Kalite bilinmiyor");
});
test("Quality merge preserves labels and size when DOM and network contain the same URL", () => {
  const url = "https://cdn.test/video.mp4";
  const entries = mergeMedia([{ kind: "video", url, size: 1000, mime: "video/mp4" }, { kind: "video", url, qualityHint: "720p", mime: "", current: true }]);
  assert.equal(entries.length, 1); assert.equal(entries[0].quality.label, "720p"); assert.equal(entries[0].size, 1000); assert.equal(entries[0].mime, "video/mp4");
});
test("Distinct signed URLs remain distinct quality choices, ordered highest first", () => {
  const entries = mergeMedia([{ kind: "video", url: "https://cdn.test/stream?quality=720&token=a" }, { kind: "video", url: "https://cdn.test/stream?quality=1080&token=b" }, { kind: "video", url: "https://cdn.test/unknown" }]);
  assert.deepEqual(entries.map(entry => entry.quality.label), ["1080p", "720p", "Kalite bilinmiyor"]);
});
test("A label found on an image is never offered as video quality", () => {
  assert.equal(videoQuality({ kind: "image", url: "https://cdn.test/image-1080p.jpg" }).height, 0);
});
