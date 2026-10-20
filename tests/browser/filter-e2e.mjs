import { createRequire } from "node:module";
import { createServer } from "node:http";
import { readFile, mkdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import assert from "node:assert/strict";
const {chromium}=createRequire(import.meta.url)(process.env.AKIS_PLAYWRIGHT||"playwright");
const root=resolve("artifacts/filter-e2e");await mkdir(root,{recursive:true});
const image=await readFile(resolve("extension/icons/128.png")),video=await readFile(resolve("tests/browser/fixtures/movie-720p.webm"));
const html='<!doctype html><meta charset="utf-8"><title>Fastdown medya filtresi</title><style>body{background:#142319;color:#d9f4e1;font:16px system-ui;padding:30px}img,video{display:block;width:400px;height:225px;object-fit:contain;background:#294a34;margin:20px 0}</style><h1>Filtrelenen medya da indirilebilir</h1><img id="image" src="/image.png"><video id="video" controls src="/movie.webm"></video>';
const server=createServer((req,res)=>{const bytes=req.url==="/image.png"?image:req.url==="/movie.webm"?video:Buffer.from(html);res.writeHead(200,{"Content-Type":req.url==="/image.png"?"image/png":req.url==="/movie.webm"?"video/webm":"text/html; charset=utf-8","Content-Length":bytes.length});res.end(bytes);});
await new Promise(r=>server.listen(0,"127.0.0.1",r));const url=`http://127.0.0.1:${server.address().port}/`,profile=join(root,"profile-"+Date.now());
let context,worker,popup,checks=0;
const check=t=>{checks++;console.log("PASS "+t);};
async function until(fn){const start=Date.now();while(!await fn()){if(Date.now()-start>15000)throw new Error("Condition timed out");await new Promise(r=>setTimeout(r,40));}}
async function launch(){context=await chromium.launchPersistentContext(profile,{channel:"chromium",headless:true,viewport:{width:1180,height:900},args:[`--disable-extensions-except=${resolve("extension")}`,`--load-extension=${resolve("extension")}`]});worker=context.serviceWorkers()[0]||await context.waitForEvent("serviceworker");await worker.evaluate(()=>{globalThis.transfers=[];chrome.runtime.sendNativeMessage=(_host,message,reply)=>{globalThis.transfers.push(message);reply({ok:true,appRunning:true,version:"1.6",fileName:message.action==="video-download"?"video."+message.videoFormat:"gizlenen.png",video:{title:"YouTube biçim testi",heights:[1080,720]}});};});}
async function panel(page){if(!popup||popup.isClosed()){popup=await context.newPage();await popup.goto(`chrome-extension://${new URL(worker.url()).host}/popup.html`);}await until(async()=>Boolean(await popup.locator("#overlay-page-address").textContent())&&await popup.locator("#refresh").isEnabled());const tabId=await worker.evaluate(async value=>(await chrome.tabs.query({})).find(t=>t.url===value).id,page.url());await worker.evaluate(id=>chrome.tabs.update(id,{active:true}),tabId);await popup.evaluate(()=>document.getElementById("refresh").click());await until(async()=>(await popup.locator("#overlay-page-address").textContent())===page.url()&&await popup.locator("#refresh").isEnabled());}
const trigger=page=>page.locator("akis-download-ui").getByRole("button",{name:"Bu medyayı Fastdown ile indir",exact:true});
async function hover(page,id){await page.locator("h1").hover();await page.locator(id).hover();await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
try{
 await launch();await worker.evaluate(()=>chrome.storage.local.set({capture:false,overlays:true,mediaFilter:{enabled:true,minKB:64,minSeconds:5,minImageEdge:160}}));
 const page=await context.newPage();await page.goto(url);await page.waitForFunction(()=>document.querySelector("video").videoHeight>0);
 await hover(page,"#image");assert.equal(await trigger(page).isVisible(),false);await hover(page,"#video");assert.equal(await trigger(page).isVisible(),false);
 await panel(page);assert.equal(await popup.locator(".media-row").count(),0);assert.match(await popup.locator("#filtered-count").textContent(),/medya filtrelendi/);
 check("Small/short media is hidden by default without removing it from discovery");
 await popup.locator("#show-filtered").check();await until(async()=>(await popup.locator(".media-row").count())===2);
 await popup.locator(".media-row").filter({hasText:"image.png"}).getByRole("button",{name:"İndir ↓"}).click();
 await until(async()=>(await worker.evaluate(()=>chrome.storage.local.get("lastTransfer"))).lastTransfer?.name==="gizlenen.png");
 assert((await worker.evaluate(()=>globalThis.transfers)).some(m=>m.action==="download"&&m.url.endsWith("image.png")));
 check("Show filtered reveals hidden media and its download button still works");
 await popup.locator(".media-filter-settings summary").click();
 for(const [id,value] of [["media-filter-kb","0"],["media-filter-edge","0"]]){await popup.locator("#"+id).fill(value);await popup.locator("#"+id).press("Tab");}
 const duration=await page.locator("video").evaluate(v=>v.duration);
 await popup.locator("#media-filter-seconds").fill(String(Math.ceil(duration)+2));await popup.locator("#media-filter-seconds").press("Tab");await popup.locator("#show-filtered").uncheck();
 await until(async()=>(await popup.locator(".media-row").count())===1);await hover(page,"#image");await trigger(page).waitFor({state:"visible"});await hover(page,"#video");assert.equal(await trigger(page).isVisible(),false);
 check("Changing thresholds keeps full-size images available while filtering a short video");
 await popup.locator("#media-filter-enabled").uncheck();await until(async()=>(await popup.locator(".media-row").count())===2);await hover(page,"#video");await trigger(page).waitFor({state:"visible"});
 check("The extension filter switch restores all media and hover controls immediately");
 await popup.screenshot({path:join(root,"filter-controls.png"),clip:{x:0,y:0,width:400,height:1200}});
 await context.close();popup=null;await launch();const reopened=await context.newPage();await reopened.goto(url);await panel(reopened);assert.equal(await popup.locator("#media-filter-enabled").isChecked(),false);
 check("Filter choices persist across a full browser restart");
 await context.route("https://www.youtube.com/**",async route=>{const path=new URL(route.request().url()).pathname;await route.fulfill({status:200,contentType:path==="/movie.webm"?"video/webm":path==="/image.png"?"image/png":"text/html; charset=utf-8",body:path==="/movie.webm"?video:path==="/image.png"?image:html});});
 const yt=await context.newPage();await yt.goto("https://www.youtube.com/watch?v=jNQXAC9IVRw");await hover(yt,"#video");await trigger(yt).click();
 const dialog=yt.locator("akis-download-ui").getByRole("dialog");await dialog.getByLabel("İndirme biçimi").selectOption("mp3");await dialog.getByRole("button",{name:"MP3 sadece ses indir",exact:true}).click();
 await until(async()=>(await worker.evaluate(()=>globalThis.transfers)).some(m=>m.action==="video-download"&&m.videoFormat==="mp3"&&m.videoHeight===0));
 check("YouTube's hover chooser forwards MP3 as audio-only rather than a video rendition");
 await panel(yt);await popup.locator("#inspect-video").click();await popup.locator("#video-options").waitFor({state:"visible"});await popup.locator("#page-format").selectOption("m4a");assert.equal(await popup.locator("#page-quality").isDisabled(),true);await popup.locator("#download-video").click();
 await until(async()=>(await worker.evaluate(()=>globalThis.transfers)).some(m=>m.action==="video-download"&&m.videoFormat==="m4a"&&m.videoHeight===0));
 await popup.locator("#page-format").selectOption("mp4");await popup.locator("#page-quality").selectOption("720");await popup.locator("#download-video").click();
 await until(async()=>(await worker.evaluate(()=>globalThis.transfers)).some(m=>m.action==="video-download"&&m.videoFormat==="mp4"&&m.videoHeight===720));
 await popup.screenshot({path:join(root,"output-formats.png"),clip:{x:0,y:0,width:400,height:1200}});
 check("Popup formats forward M4A without video and MP4 with the selected 720p resolution");
 console.log(`\n${checks} format/filter browser checks passed.`);
}finally{if(context)await context.close();server.closeAllConnections();await new Promise(r=>server.close(r));}
