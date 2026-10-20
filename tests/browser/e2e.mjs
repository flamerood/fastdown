import { createRequire } from "node:module";
import { createServer } from "node:http";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { readFile, mkdir, stat } from "node:fs/promises";
import { resolve, join } from "node:path";
import assert from "node:assert/strict";

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require("playwright")); }
catch { ({ chromium } = require(process.env.AKIS_PLAYWRIGHT || "playwright")); }
const root = resolve("artifacts/browser-e2e");
await mkdir(root, { recursive: true });
const fixture = spawn("dotnet", [resolve("tests/Fastdown.Tests/bin/Release/net10.0/Fastdown.Tests.dll"), "--browser-fixture"], { cwd: resolve("."), stdio: ["pipe", "pipe", "pipe"], windowsHide: true });
let fixtureError = "";
fixture.stderr.on("data", data => { fixtureError += data; });
const lines = createInterface({ input: fixture.stdout });
const ready = await Promise.race([
  new Promise((accept, reject) => { lines.once("line", line => { try { accept(JSON.parse(line)); } catch { reject(new Error(line)); } }); fixture.once("exit", code => reject(new Error(`Fixture exited ${code}: ${fixtureError}`))); }),
  new Promise((_, reject) => setTimeout(() => reject(new Error("Fixture startup timeout")), 15000).unref())
]);
const image = await readFile(resolve("extension/icons/128.png"));
const video1080 = await readFile(resolve("tests/browser/fixtures/movie-1080p.webm"));
const video720 = await readFile(resolve("tests/browser/fixtures/movie-720p.webm"));
const video480 = await readFile(resolve("tests/browser/fixtures/movie-480p.webm"));
const requested = new Map();
const server = createServer((req, res) => {
  requested.set(req.url,(requested.get(req.url)||0)+1);
  const opaque={"/opaque-a.webm":video480,"/opaque-b.webm":video720,"/opaque-c.webm":video1080,"/anonymous-one.webm":video720,"/anonymous-two.webm":video1080};
  if(req.url==="/extractor-page"){
    res.writeHead(200,{"Content-Type":"text/html; charset=utf-8"});res.end('<!doctype html><html><title>Sesli video kalite testi</title><body style="background:#15231a;color:#d9f3e2;font:18px system-ui;padding:30px"><h1>Video ve ses birleştirme</h1><video controls width="640"><source src="/extractor.mpd" type="application/dash+xml"></video></body></html>');return;
  }
  if(req.url==="/extractor.mpd") {res.writeHead(200,{"Content-Type":"application/dash+xml"});res.end('<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT2S" minBufferTime="PT1S"><Period><AdaptationSet mimeType="video/mp4" contentType="video"><Representation id="720" width="1280" height="720" codecs="avc1.42C01F" bandwidth="100000"><BaseURL>'+ready.url+'media/split-video-720.mp4</BaseURL></Representation><Representation id="1080" width="1920" height="1080" codecs="avc1.42C028" bandwidth="200000"><BaseURL>'+ready.url+'media/split-video-1080.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet mimeType="audio/mp4" contentType="audio"><Representation id="audio" codecs="mp4a.40.2" bandwidth="64000"><BaseURL>'+ready.url+'media/split-audio.m4a</BaseURL></Representation></AdaptationSet></Period></MPD>');return;}
  if(opaque[req.url]){res.writeHead(200,{"Content-Type":"video/webm","Content-Length":opaque[req.url].length});res.end(opaque[req.url]);return;}
  if(req.url==="/source-data"){res.writeHead(200,{"Content-Type":"application/json"});res.end(JSON.stringify({urls:[{url:"/opaque-a.webm",label:"480p"},{url:"/opaque-b.webm",label:"720p"},{url:"/opaque-c.webm",label:"1080p"}]}));return;}
  if(req.url==="/no-label-data"){res.writeHead(200,{"Content-Type":"application/json"});res.end(JSON.stringify({sources:[{src:"/anonymous-one.webm",type:"video/webm"},{src:"/anonymous-two.webm",type:"video/webm"}]}));return;}
  if(req.url==="/master.m3u8"){res.writeHead(200,{"Content-Type":"application/vnd.apple.mpegurl"});res.end('#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080\n/hls/high.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720\n/hls/low.m3u8');return;}
  if(req.url==="/framed"){res.writeHead(200,{"Content-Type":"text/html"});res.end(`<html><title>Embedded player test</title><iframe style="width:900px;height:650px" src="http://localhost:${server.address().port}/api-player"></iframe></html>`);return;}
  if(["/api-player","/xhr-player","/unlabelled-player","/stream-player"].includes(req.url)){
    let code="fetch('/source-data').then(r=>r.json()).then(data=>{document.querySelector('video').src=data.urls[1].url;});";
    if(req.url==="/xhr-player")code="const request=new XMLHttpRequest();request.open('GET','/source-data');request.responseType='json';request.onload=()=>{document.querySelector('video').src=request.response.urls[1].url;};request.send();";
    if(req.url==="/unlabelled-player")code="fetch('/no-label-data').then(r=>r.json()).then(data=>{document.querySelector('video').src=data.sources[0].src;});";
    if(req.url==="/stream-player")code="document.querySelector('video').src='/opaque-b.webm';fetch('/master.m3u8').then(r=>r.text());";
    res.writeHead(200,{"Content-Type":"text/html; charset=utf-8"});res.end(`<!doctype html><html><meta charset="utf-8"><title>General source discovery</title><style>body{font:16px system-ui;background:#11171b;color:#eff7f1;padding:30px}video{width:800px;height:450px;background:#254333}</style><h1>Genel kaynak keşfi</h1><p>Video öğesinde yalnızca seçili sürüm var. Diğerleri kaynak verisinde.</p><video controls preload="auto"></video><script>${code}</script></html>`);return;
  }
  if (req.url.startsWith("/image.png")) { res.writeHead(200, { "Content-Type": "image/png", "Content-Length": image.length }); res.end(image); return; }
  if (req.url === "/video.mp4") { res.writeHead(200, { "Content-Type": "video/mp4", "Content-Length": 8192 }); res.end(Buffer.alloc(8192, 42)); return; }
  if (["/variant-full.webm", "/variant-small.webm"].includes(req.url)) { const data = req.url === "/variant-full.webm" ? video1080 : video720; res.writeHead(200, { "Content-Type": "video/webm", "Content-Length": data.length }); res.end(data); return; }
  if (req.url === "/quality") { res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" }); res.end(`<!doctype html><html lang="tr"><meta charset="utf-8"><title>Kalite seçimi</title><style>body{font:16px system-ui;background:#11171b;color:#e9f3ed;padding:45px}h1{font-size:30px}p{color:#90a398}video{display:block;width:800px;height:450px;background:#23472c;border-radius:12px}</style><h1>Fastdown · Video kalitesi seçimi</h1><p>Bu oynatıcı iki gerçek video dosyası sunar: 1920×1080 ve 1280×720.</p><video id="quality-video" controls preload="auto"><source src="/variant-full.webm" type="video/webm" label="1080p"><source src="/variant-small.webm" type="video/webm" label="720p"></video></html>`); return; }
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
  res.end(`<!doctype html><html lang="tr"><meta charset="utf-8"><title>Fastdown eklenti testi</title><style>body{font:16px system-ui;background:#11171b;color:#e9f3ed;padding:50px}h1{font-size:32px}p{color:#90a398}main{display:flex;gap:28px;margin:30px 0}img,video{width:360px;height:220px;object-fit:contain;background:#24392c;border-radius:10px}a{display:inline-block;background:#99f0bf;color:#10251a;padding:14px 20px;border-radius:8px;text-decoration:none}</style><h1>Fastdown · Tarayıcı bağlantısı</h1><p>Yerel test sayfası: resim, video ve otomatik indirme aktarımı</p><main><img id="image" src="/image.png" alt="Fastdown test resmi"><video id="video" controls src="/video.mp4"></video></main><a id="automatic" href="${ready.url}attachment" download>Normal dosya indir</a><div id="dynamic"></div></html>`);
});
await new Promise(accept => server.listen(0, "127.0.0.1", accept));
let context;
let checks = 0;
const check = name => { checks++; console.log("PASS  " + name); };
async function until(predicate, timeout = 20000) { const start = Date.now(); while (true) { try { if (await predicate()) return; } catch {} if (Date.now() - start > timeout) throw new Error("Timed out waiting for condition"); await new Promise(r => setTimeout(r, 100)); } }
async function jobs() { return JSON.parse(await readFile(ready.library, "utf8")).Jobs; }
try {
  const extension = resolve("extension");
  context = await chromium.launchPersistentContext(join(root, "profile-" + Date.now()), { channel: "chromium", headless: true, acceptDownloads: true, viewport: { width: 1180, height: 800 }, args: [`--disable-extensions-except=${extension}`, `--load-extension=${extension}`] });
  let worker = context.serviceWorkers()[0];
  if (!worker) worker = await context.waitForEvent("serviceworker", { timeout: 15000 });
  const id = new URL(worker.url()).host;
  await worker.evaluate(() => chrome.storage.local.set({ capture: true, overlays: true, mediaFilter: { enabled: false } }));
  assert.equal(id, "cjfejnimgnbehgbgjejbkkdifjoakfnd");
  check("Real Chromium loads the MV3 extension with stable identity");
  const ping = await worker.evaluate(() => new Promise(resolve => chrome.runtime.sendNativeMessage("com.akis.download_manager", { action: "ping" }, reply => resolve({ reply, error: chrome.runtime.lastError?.message }))));
  assert.equal(ping.reply?.ok, true, JSON.stringify(ping)); assert.equal(ping.reply.appRunning, true);
  check("Extension → registered native host → current-user named pipe → download manager");
  const page = await context.newPage();
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.locator("#image").hover();
  const overlay = page.locator("akis-download-ui").getByRole("button", { name: "Bu medyayı Fastdown ile indir" });
  await overlay.waitFor({ state: "visible" });
  await page.screenshot({ path: join(root, "image-overlay.png") });
  await overlay.click();
  await until(async () => (await jobs()).some(j => j.FileName === "image.png" && j.State === "Completed"));
  assert.deepEqual(await readFile(join(ready.downloads, "image.png")), image);
  check("Hovering an image shows the button and downloads identical bytes through native host");
  await page.locator("#video").hover(); await overlay.waitFor({ state: "visible" }); await overlay.click();
  await page.locator("akis-download-ui").getByRole("button", { name: /Kalite bilinmiyor indir.*video.mp4/ }).click();
  await until(async () => (await jobs()).some(j => j.FileName === "video.mp4" && j.State === "Completed"));
  assert.equal((await stat(join(ready.downloads, "video.mp4"))).size, 8192);
  check("Video overlay forwards the direct video source");
  const qualityPage = await context.newPage();
  await qualityPage.goto(`http://127.0.0.1:${server.address().port}/quality`);
  await qualityPage.waitForFunction(() => document.querySelector("video").videoHeight === 1080 && document.querySelector("video").readyState >= 2);
  await qualityPage.locator("video").hover();
  await qualityPage.locator("akis-download-ui").getByRole("button", { name: "Bu medyayı Fastdown ile indir" }).click();
  const qualityDialog = qualityPage.locator("akis-download-ui").getByRole("dialog", { name: "Video kalitesi seç" });
  await qualityDialog.getByRole("button", { name: /1080p indir/ }).waitFor();
  await qualityDialog.getByRole("button", { name: /720p indir/ }).waitFor();
  const labels = await qualityDialog.locator(".choice b").allTextContents();
  assert(labels[0].startsWith("1080p")); assert(labels[1].startsWith("720p"));
  assert(!(await jobs()).some(j => /variant-/.test(j.Url)), "Opening the chooser must not start a download");
  await qualityPage.screenshot({ path: join(root, "quality-chooser.png") });
  check("The player's real 1080p dimensions and unplayed 720p source appear in a sorted chooser");
  await qualityDialog.getByRole("button", { name: /720p indir/ }).click();
  await until(async () => (await jobs()).some(j => j.FileName === "variant-small.webm" && j.State === "Completed"));
  assert.deepEqual(await readFile(join(ready.downloads, "variant-small.webm")), video720);
  assert(!(await jobs()).some(j => /variant-full/.test(j.Url)), "720p selection silently downloaded the 1080p URL");
  check("Choosing 720p downloads exactly the 720p bytes and never enqueues 1080p");
  await page.evaluate(() => { const img = document.createElement("img"); img.id = "late-image"; img.src = "/image.png?dynamic"; img.style.cssText = "display:block;width:200px;height:130px"; document.querySelector("#dynamic").append(img); });
  await page.locator("#late-image").hover(); await overlay.waitFor({ state: "visible" });
  check("Dynamically inserted media receives the overlay without reloading");
  await page.locator("#automatic").click();
  await until(async () => (await jobs()).some(j => j.FileName === "automatic.bin" && j.State === "Completed"), 25000);
  assert.equal((await stat(join(ready.downloads, "automatic.bin"))).size, 6291456);
  const transfer = await worker.evaluate(() => chrome.storage.local.get("lastTransfer"));
  assert.equal(transfer.lastTransfer.ok, true);
  const browserCopy = await worker.evaluate(() => chrome.downloads.search({ filenameRegex: "automatic\\.bin$" }));
  assert.equal(browserCopy.length, 0);
  check("A normal browser download is paused, accepted by Fastdown and removed from browser queue");
  const popup = await context.newPage();
  popup.on("pageerror", error => console.error("POPUP ERROR: " + error.message));
  await popup.goto(`chrome-extension://${id}/popup.html`);
  try { await popup.getByText("Fastdown açık ve bağlı", { exact: false }).waitFor({ timeout: 10000 }); }
  catch (error) { console.error("POPUP STATE: " + await popup.locator("body").innerText()); await popup.screenshot({ path: join(root, "popup-error.png") }); throw error; }
  await popup.screenshot({ path: join(root, "popup.png"), clip: { x: 0, y: 0, width: 400, height: 700 } });
  check("Real extension popup connects and renders");
  const qualityTab = await worker.evaluate(async url => (await chrome.tabs.query({})).find(tab => tab.url === url).id, qualityPage.url());
  await worker.evaluate(id => chrome.tabs.update(id, { active: true }), qualityTab);
  await popup.evaluate(() => document.getElementById("refresh").click());
  await until(async () => (await popup.locator(".media-row").count()) >= 2);
  await popup.locator("#quality").selectOption("720");
  assert.equal(await popup.locator(".media-row").count(), 1);
  assert.match(await popup.locator(".media-row").innerText(), /720p/);
  await popup.screenshot({ path: join(root, "quality-popup.png"), clip: { x: 0, y: 0, width: 400, height: 700 } });
  check("Popup quality filter lists only 720p when selected");
  await popup.locator("#quality").selectOption("1080");
  await popup.locator(".media-row button").click();
  await until(async () => (await jobs()).some(j => j.FileName === "variant-full.webm" && j.State === "Completed"));
  assert.deepEqual(await readFile(join(ready.downloads, "variant-full.webm")), video1080);
  check("Choosing 1080p in the popup downloads exactly the 1080p source");
  const apiPage=await context.newPage();await apiPage.goto(`http://127.0.0.1:${server.address().port}/api-player`);
  await apiPage.waitForFunction(()=>document.querySelector("video").videoHeight===720);
  const highBefore=requested.get("/opaque-c.webm")||0;
  await apiPage.locator("video").hover();await apiPage.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir"}).click();
  const apiDialog=apiPage.locator("akis-download-ui").getByRole("dialog");
  for(const quality of [480,720,1080])await apiDialog.getByRole("button",{name:new RegExp(`${quality}p indir`)}).waitFor();
  assert.equal(requested.get("/opaque-c.webm")||0,highBefore,"Discovering an unplayed quality should not play or download it");
  await apiPage.screenshot({path:join(root,"general-source-discovery.png")});
  check("Fetch JSON discovery lists unplayed 480/720/1080p with opaque URLs before playing them");
  await apiDialog.getByRole("button",{name:/480p indir/}).click();
  await until(async()=>(await jobs()).some(j=>j.FileName==="opaque-a.webm"&&j.State==="Completed"));
  assert.deepEqual(await readFile(join(ready.downloads,"opaque-a.webm")),video480);
  check("A previously unplayed API-only quality downloads the exact selected file");
  const xhrPage=await context.newPage();await xhrPage.goto(`http://127.0.0.1:${server.address().port}/xhr-player`);await xhrPage.waitForFunction(()=>document.querySelector("video").videoHeight===720);
  await xhrPage.locator("video").hover();await xhrPage.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir"}).click();
  await xhrPage.locator("akis-download-ui").getByRole("button",{name:/1080p indir/}).waitFor();
  check("XHR source responses work through the same generic discovery path");
  const unlabeled=await context.newPage();await unlabeled.goto(`http://127.0.0.1:${server.address().port}/unlabelled-player`);await unlabeled.waitForFunction(()=>document.querySelector("video").videoHeight===720);
  await unlabeled.locator("video").hover();await unlabeled.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir"}).click();
  await unlabeled.locator("akis-download-ui").getByRole("button",{name:/1080p indir.*anonymous-two/}).waitFor();
  assert.equal(await unlabeled.locator("video").evaluate(v=>v.videoHeight),720);
  check("An unplayed unlabelled file gets its real 1080p resolution from the binary header");
  const framed=await context.newPage();await framed.goto(`http://127.0.0.1:${server.address().port}/framed`);
  await framed.frameLocator("iframe").locator("video").waitFor();
  const framedTab=await worker.evaluate(async url=>(await chrome.tabs.query({})).find(tab=>tab.url===url).id,framed.url());
  await worker.evaluate(id=>chrome.tabs.update(id,{active:true}),framedTab);
  await popup.evaluate(()=>{document.getElementById("quality").value="all";document.getElementById("refresh").click();});
  await until(async()=>(await popup.locator(".quality").allTextContents()).some(t=>t.startsWith("480p"))&&(await popup.locator(".quality").allTextContents()).some(t=>t.startsWith("1080p")));
  check("Popup aggregates source alternatives and intrinsic dimensions from a cross-origin iframe");
  const streaming=await context.newPage();await streaming.goto(`http://127.0.0.1:${server.address().port}/stream-player`);await streaming.waitForFunction(()=>document.querySelector("video").videoHeight===720);
  await streaming.locator("video").hover();await streaming.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir"}).click();
  const streamChoice=streaming.locator("akis-download-ui").getByRole("button",{name:/1080p indir.*high.m3u8/});await streamChoice.waitFor();assert.equal(await streamChoice.isDisabled(),true);
  check("HLS master qualities are detected and kept distinct from supported direct-file downloads");
  const extracted=await context.newPage();await extracted.goto(`http://127.0.0.1:${server.address().port}/extractor-page`);
  const extractedTab=await worker.evaluate(async url=>(await chrome.tabs.query({})).find(tab=>tab.url===url).id,extracted.url());
  await worker.evaluate(id=>chrome.tabs.update(id,{active:true}),extractedTab);
  await popup.evaluate(()=>document.getElementById("refresh").click());
  await until(async()=>(await popup.locator("#refresh").isEnabled())&&await popup.locator("#page-video").isVisible());
  await popup.locator("#inspect-video").click();
  try {await popup.locator("#video-options").waitFor({state:"visible",timeout:30000});}
  catch(error){console.error("VIDEO PANEL: "+await popup.locator("body").innerText());throw error;}
  assert.deepEqual(await popup.locator("#page-quality option").allTextContents(),["En iyi kalite + ses","1080p + ses","720p + ses"]);
  check("Page extraction through the real native bridge exposes both 1080p and 720p with audio");
  await popup.locator("#page-quality").selectOption("720");
  await popup.screenshot({path:join(root,"video-page-quality.png"),clip:{x:0,y:0,width:400,height:800}});
  await popup.locator("#download-video").click();
  await until(async()=>(await jobs()).some(j=>j.IsVideoPage&&j.VideoHeight===720&&j.State==="Completed"),60000);
  const merged=(await jobs()).find(j=>j.IsVideoPage&&j.VideoHeight===720&&j.State==="Completed");
  assert.equal(merged.VideoFormat,"mp4");
  assert.match(merged.FileName,/\.mp4$/i);
  assert.equal((await stat(join(ready.downloads,merged.FileName))).size,merged.TotalBytes);
  assert(!((await jobs()).some(j=>j.IsVideoPage&&j.VideoHeight===1080)),"720p choice queued an unselected 1080p video");
  check("The selected 720p page video is downloaded and merged into one MP4 through the popup");
  await worker.evaluate(() => chrome.storage.local.set({ capture: false }));
  const fallback = await context.newPage(); await fallback.goto(`http://127.0.0.1:${server.address().port}/`);
  const browserDownload = fallback.waitForEvent("download"); await fallback.locator("#automatic").click();
  const ordinary = await browserDownload; await ordinary.saveAs(join(root, "browser-original.bin"));
  assert.equal((await stat(join(root, "browser-original.bin"))).size, 6291456);
  assert.equal((await jobs()).filter(j => j.FileName.startsWith("automatic")).length, 1);
  check("Turning capture off leaves the ordinary browser download intact");
  console.log(`\n${checks} real-browser checks passed. Downloads isolated in ${ready.downloads}`);
} finally {
  if (context) await context.close();
  server.closeAllConnections(); await new Promise(accept => server.close(accept));
  fixture.stdin.end("stop\n");
  await new Promise(accept => fixture.once("exit", accept));
  lines.close();
}
