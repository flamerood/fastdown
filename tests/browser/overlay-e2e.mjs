import { createRequire } from "node:module";
import { createServer } from "node:http";
import { readFile, mkdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import assert from "node:assert/strict";
const require=createRequire(import.meta.url);
let chromium;
try {({chromium}=require("playwright"));}
catch {({chromium}=require(process.env.AKIS_PLAYWRIGHT||"playwright"));}
const root=resolve("artifacts/overlay-e2e");await mkdir(root,{recursive:true});
const image=await readFile(resolve("extension/icons/128.png"));
const video=await readFile(resolve("tests/browser/fixtures/movie-720p.webm"));
const server=createServer((req,res)=>{
  if(req.url.startsWith("/image.png")){res.writeHead(200,{"Content-Type":"image/png","Content-Length":image.length});res.end(image);return;}
  if(req.url.startsWith("/movie.webm")){res.writeHead(200,{"Content-Type":"video/webm","Content-Length":video.length});res.end(video);return;}
  const content=req.url==="/framed"?`<iframe style="width:1000px;height:600px" src="http://localhost:${server.address().port}/child"></iframe>`:'<main><img id="image" width="280" height="180" src="/image.png"><video id="video" width="440" height="260" controls src="/movie.webm"></video></main><div id="dynamic"></div>';
  res.writeHead(200,{"Content-Type":"text/html; charset=utf-8"});res.end(`<!doctype html><title>Fastdown gizleme testi</title><style>body{background:#152019;color:#d8efdf;font:16px system-ui;padding:30px}main{display:flex;gap:25px}img,video{background:#294333;object-fit:contain}iframe{border:0}h1{font-size:24px}</style><h1>Medya düğmelerini gizleme</h1>${content}`);
});
await new Promise(r=>server.listen(0,"127.0.0.1",r));
const base=`http://127.0.0.1:${server.address().port}`,profile=join(root,"profile-"+Date.now());
let context,worker,popup,checks=0;
const check=message=>{checks++;console.log("PASS "+message);};
async function until(fn){const start=Date.now();while(!await fn()){if(Date.now()-start>10000)throw new Error("Timed out waiting for settings");await new Promise(r=>setTimeout(r,40));}}
const download=page=>page.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir",exact:true});
const dismiss=page=>page.locator("akis-download-ui").getByRole("button",{name:"Bu medyada bu seferlik gizle",exact:true});
async function hoverHidden(page,selector){await page.locator("h1").hover();await page.locator(selector).hover();await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));assert.equal(await download(page).isVisible(),false);}
async function hoverShown(page,selector){await page.locator("h1").hover();await page.locator(selector).hover();await download(page).waitFor({state:"visible"});}
async function launch(){context=await chromium.launchPersistentContext(profile,{channel:"chromium",headless:true,viewport:{width:1180,height:800},args:[`--disable-extensions-except=${resolve("extension")}`,`--load-extension=${resolve("extension")}`]});worker=context.serviceWorkers()[0]||await context.waitForEvent("serviceworker");}
async function showSettings(page){
  if(!popup||popup.isClosed()){popup=await context.newPage();popup.on("pageerror",error=>console.error("POPUP "+error.message));await popup.goto(`chrome-extension://${new URL(worker.url()).host}/popup.html`);}
  await until(async()=>Boolean(await popup.locator("#overlay-page-address").textContent())&&await popup.locator("#refresh").isEnabled());
  const id=await worker.evaluate(async url=>(await chrome.tabs.query({})).find(t=>t.url===url).id,page.url());
  await worker.evaluate(id=>chrome.tabs.update(id,{active:true}),id);
  await popup.evaluate(()=>document.getElementById("refresh").click());
  try {await until(async()=>(await popup.locator("#overlay-page-address").textContent())===page.url());}
  catch(error){console.error("PAGE EXPECTED "+page.url()+" ACTUAL "+await popup.locator("#overlay-page-address").textContent());console.error(await popup.locator("body").innerText());throw error;}
  await popup.locator("#overlay-preferences").evaluate(node=>node.open=true);
}
async function ruleCount(count){await until(async()=>(await worker.evaluate(()=>chrome.storage.local.get("overlayRules"))).overlayRules?.length===count);}
try{
  await launch();await worker.evaluate(()=>chrome.storage.local.set({capture:false,overlays:true,overlayRules:[],mediaFilter:{enabled:false}}));
  const page=await context.newPage();await page.goto(base+"/one");await hoverShown(page,"#image");
  await dismiss(page).waitFor({state:"visible"});await page.screenshot({path:join(root,"dismiss-button.png")});
  await dismiss(page).click();await hoverHidden(page,"#image");
  await page.mouse.move(10,10);await hoverHidden(page,"#image");
  assert.equal((await worker.evaluate(()=>chrome.storage.local.get("lastTransfer"))).lastTransfer,undefined);
  check("The × hides only that media and never starts a download on click or later hover");
  await hoverShown(page,"#video");await dismiss(page).click();
  await page.evaluate(()=>document.querySelector("#video").src="/movie.webm?another-quality");
  await hoverHidden(page,"#video");
  check("Dismissing a video keeps it hidden even when its quality/source changes");
  await page.evaluate(()=>{const old=document.querySelector("#image");const replacement=old.cloneNode();old.replaceWith(replacement);});await hoverHidden(page,"#image");
  await page.evaluate(()=>{const item=document.createElement("img");item.id="other";item.width=280;item.height=180;item.src="/image.png?different";document.querySelector("#dynamic").append(item);});await hoverShown(page,"#other");
  check("Replaced media with the same URL stays dismissed while other images remain available");
  await page.reload();await hoverShown(page,"#image");await hoverShown(page,"#video");
  check("Reloading the page restores media hidden with ×");
  await showSettings(page);await popup.locator("#hide-page").check();await ruleCount(1);
  await hoverHidden(page,"#image");await hoverHidden(page,"#video");
  await page.reload();await hoverHidden(page,"#image");
  check("The exact-page setting hides existing controls immediately and survives a reload");
  await page.evaluate(()=>history.pushState({},"","/two"));await hoverShown(page,"#image");
  await page.evaluate(()=>history.pushState({},"","/one"));await hoverHidden(page,"#image");
  check("SPA navigation honors exact page rules and restores controls on a different page");
  const other=await context.newPage();await other.goto(base+"/two");await hoverShown(other,"#image");
  await showSettings(other);await popup.locator("#hide-site").check();await ruleCount(2);
  await hoverHidden(other,"#image");
  await other.goto(base+"/three");await hoverHidden(other,"#image");
  const otherOrigin=await context.newPage();await otherOrigin.goto(`http://localhost:${server.address().port}/one`);await hoverShown(otherOrigin,"#image");
  check("A site exclusion covers its other pages without hiding a different origin");
  const framed=await context.newPage();await framed.goto(base+"/framed");const frame=framed.frames().find(f=>f.url().includes("/child"));
  await frame.waitForSelector("#image");await hoverHidden(frame,"#image");await hoverHidden(frame,"#video");
  check("A disabled parent page also suppresses controls in a cross-origin embedded player");
  await showSettings(framed);await popup.screenshot({path:join(root,"persistent-settings.png"),clip:{x:0,y:0,width:400,height:850}});
  await context.close();popup=null;
  await launch();const reopened=await context.newPage();await reopened.goto(base+"/two");await hoverHidden(reopened,"#image");await showSettings(reopened);
  assert.equal(await popup.locator("#hide-site").isChecked(),true);assert.equal(await popup.locator(".rule-row").count(),2);
  check("Permanent exclusions and their management list survive a full browser restart");
  await popup.getByRole("button",{name:"Gizlemeyi kaldır: "+base,exact:true}).click();await ruleCount(1);await hoverShown(reopened,"#image");
  await reopened.goto(base+"/one");await hoverHidden(reopened,"#image");await showSettings(reopened);
  await popup.getByRole("button",{name:"Gizlemeyi kaldır: "+base+"/one",exact:true}).click();await ruleCount(0);await hoverShown(reopened,"#image");
  check("Removing an exclusion restores controls and preserves other exclusions until removed");
  // A page-only rule must reach a child even without a site-wide rule.
  await reopened.goto(base+"/framed");const child=reopened.frames().find(f=>f.url().includes("/child"));await child.waitForSelector("#image");await hoverShown(child,"#image");
  await showSettings(reopened);await popup.locator("#hide-page").check();await ruleCount(1);await hoverHidden(child,"#image");
  await reopened.reload();const childAfter=reopened.frames().find(f=>f.url().includes("/child"));await childAfter.waitForSelector("#image");await hoverHidden(childAfter,"#image");
  check("Page-only exclusions apply to cross-origin child frames both live and after reload");
  const normal=await context.newPage();await normal.goto(base+"/two");await hoverShown(normal,"#image");await showSettings(normal);
  await popup.locator("#overlays").uncheck();await hoverHidden(normal,"#image");
  await popup.locator("#overlays").check();await hoverShown(normal,"#image");
  check("The global overlay switch still hides and restores controls immediately");
  // Exercise the download half of the new two-button control without adding fixtures to the user's desktop library.
  await worker.evaluate(()=>{globalThis.overlayTransfers=[];chrome.runtime.sendNativeMessage=(_host,message,reply)=>{globalThis.overlayTransfers.push(message);reply({ok:true,fileName:"resim.png",appRunning:true});};});
  await download(normal).click();await until(async()=>(await worker.evaluate(()=>chrome.storage.local.get("lastTransfer"))).lastTransfer?.name==="resim.png");
  const transfers=await worker.evaluate(()=>globalThis.overlayTransfers);
  assert.equal(transfers.length,1);assert.equal(transfers[0].action,"download");assert.equal(transfers[0].url,base+"/image.png");
  check("The download half of the split control still sends the selected media to the bridge");
  console.log(`\n${checks} overlay browser checks passed.`);
}finally{if(context)await context.close();server.closeAllConnections();await new Promise(r=>server.close(r));}
