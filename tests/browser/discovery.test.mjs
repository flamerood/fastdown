import test from "node:test";
import assert from "node:assert/strict";
import {readFile} from "node:fs/promises";
import "../../extension/source-parser.js";
import {mergeMedia,videoQuality} from "../../extension/shared.js";
import {mp4Dimensions,webmDimensions,probeVideo,boundedResponse} from "../../extension/video-probe.js";
const parser=globalThis.FastdownSources, base="https://cdn.example/player/config.json";

test("General JSON source lists expose unplayed 480/720/1080p URLs",()=>{
  const entries=parser.extract({urls:[{label:"480p",url:"https://a.example/a.mp4"},{label:"720p",url:"https://b.example/b.mp4"},{label:"1080p",url:"https://c.example/c.mp4"}]},base);
  assert.deepEqual(mergeMedia(entries).map(e=>e.quality.label),["1080p","720p","480p"]);
});
test("Nested playlists, Video.js sources and quality-keyed maps use the same parser",()=>{
  const shapes=[{playlist:[{sources:[{file:"/a.mp4",label:"1080p"}]}]},{options:{sources:[{src:"/a.mp4",height:1080,width:1920,type:"video/mp4"}]}},{qualities:{"1080p":"/a.mp4"}}];
  for(const data of shapes){const entries=parser.extract(data,base);assert.equal(entries.length,1);assert.equal(videoQuality(entries[0]).label,"1080p");}
});
test("Extensionless typed video sources and relative URLs are detected",()=>{
  const [entry]=parser.extract({sources:[{src:"../stream?id=1",type:"video/mp4",height:720}]},base);
  assert.equal(entry.url,"https://cdn.example/stream?id=1");assert.equal(videoQuality(entry).label,"720p");
});
test("Page data extraction never forwards profile fields, credentials, keys or non-video resources",()=>{
  const entries=parser.extract({email:"private@example.com",token:"secret",cookies:"private",sources:[{src:"https://u:p@cdn.example/a.mp4"},{src:"file:///secret.mp4"},{src:"javascript:alert(1)"},{src:"https://cdn.example/a.jpg"},{file:"https://cdn.example/v.mp4",label:"720p",user:"private"}]},base);
  assert.equal(entries.length,1);assert(!JSON.stringify(entries).includes("private"));assert(!JSON.stringify(entries).includes("secret"));
});
test("Plain inline source objects are parsed without evaluating JavaScript",()=>{
  const entries=parser.parseText("player.setup({sources:[{file:'https://cdn.example/a.mp4',label:'1080p'},{src:'https://cdn.example/b.mp4',height:720}]});throw Error('never execute')",base);
  assert.deepEqual(mergeMedia(entries).map(e=>e.quality.label),["1080p","720p"]);
});
test("Source parsing is bounded and survives cyclic or malformed data",()=>{
  const cyclic={};cyclic.self=cyclic;assert.deepEqual(parser.extract(cyclic,base),[]);
  assert.deepEqual(parser.parseText("x".repeat(parser.limit+1),base),[]);
  assert.deepEqual(parser.parseText("{not json",base),[]);
});
test("HLS master playlists enumerate every rendition, including unrequested variants",()=>{
  const entries=parser.hls('#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=5000000,CODECS="avc1.640028,mp4a.40.2",RESOLUTION=1920x1080\nhigh/index.m3u8?sig=one\n#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720\nlow/index.m3u8?sig=two',"https://cdn.example/master.m3u8");
  assert.deepEqual(entries.map(e=>videoQuality(e).label),["1080p","720p"]);assert.equal(entries[0].url,"https://cdn.example/high/index.m3u8?sig=one");assert(entries.every(e=>e.stream==="hls"));
});
test("HLS encryption is identified without reading or forwarding encryption keys",()=>{
  const entries=parser.hls('#EXTM3U\n#EXT-X-SESSION-KEY:METHOD=SAMPLE-AES,URI="https://secret.example/license"\n#EXT-X-STREAM-INF:RESOLUTION=1280x720\na.m3u8',base);
  assert.equal(entries[0].protected,true);assert(!JSON.stringify(entries).includes("license"));
});
test("DASH qualities inherit dimensions and preserve separate representation identities",()=>{
  const xml='<MPD><BaseURL>https://cdn.example/</BaseURL><Period><AdaptationSet mimeType="video/mp4" width="1920" height="1080"><Representation id="hi" bandwidth="5000"/><Representation id="lo" width="1280" height="720" bandwidth="2500"/></AdaptationSet><AdaptationSet contentType="audio"><Representation id="audio"/></AdaptationSet></Period></MPD>';
  const entries=parser.dash(xml,base);assert.equal(entries.length,2);assert.equal(mergeMedia(entries).length,2);assert.deepEqual(entries.map(e=>videoQuality(e).label),["1080p","720p"]);
});
test("DASH ContentProtection is inherited and XML external entities are rejected",()=>{
  const xml='<MPD><Period><AdaptationSet contentType="video"><ContentProtection schemeIdUri="urn:drm"/><Representation id="one" height="720"><BaseURL>a.mp4</BaseURL></Representation></AdaptationSet></Period></MPD>';
  assert.equal(parser.dash(xml,base)[0].protected,true);assert.deepEqual(parser.dash('<!DOCTYPE a [<!ENTITY x SYSTEM "file:///secret">]>'+xml,base),[]);
});
test("Real WebM header metadata reveals both resolutions without video playback",async()=>{
  for(const height of [720,1080]){const data=await readFile(new URL(`./fixtures/movie-${height}p.webm`,import.meta.url));assert.deepEqual(webmDimensions(data),{width:height*16/9,height});}
});
function box(type,body){const result=Buffer.alloc(body.length+8);result.writeUInt32BE(result.length);result.write(type,4,4,"ascii");body.copy(result,8);return result;}
function moov(width,height){const body=Buffer.alloc(84);body.writeUInt32BE(width*65536,76);body.writeUInt32BE(height*65536,80);return box("moov",box("trak",box("tkhd",body)));}
test("MP4 track metadata reports dimensions even when no quality label exists",()=>{
  assert.deepEqual(mp4Dimensions(moov(1920,1080)).dimensions,{width:1920,height:1080});
});
test("MP4 probing skips the large media payload and reads the trailing metadata atom",async()=>{
  const ftyp=box("ftyp",Buffer.from("isom0000")),mdat=box("mdat",Buffer.alloc(1024*1024)),movie=Buffer.concat([ftyp,mdat,moov(1280,720)]),requests=[];
  const fetcher=async(_url,options)=>{const [,from,to]=options.headers.Range.match(/bytes=(\d+)-(\d+)/);const start=Number(from),end=Math.min(Number(to),movie.length-1);requests.push([start,end]);return new Response(movie.subarray(start,end+1),{status:206,headers:{"content-range":`bytes ${start}-${end}/${movie.length}`}});};
  assert.deepEqual(await probeVideo("https://cdn.example/opaque",fetcher),{width:1280,height:720});assert.equal(requests.length,2);assert.equal(requests[1][0],ftyp.length+mdat.length);assert(requests.reduce((sum,[a,b])=>sum+b-a+1,0)<256*1024);
});
test("Unknown and malformed files do not fabricate dimensions",()=>{
  for(const bytes of [new Uint8Array(),new Uint8Array([1,2,3]),Buffer.alloc(128,42)]){assert.equal(mp4Dimensions(bytes).dimensions,null);assert.equal(webmDimensions(bytes),null);}
});
test("Bounded reads cancel a server that ignores Range instead of downloading the full file",async()=>{
  let cancelled=false;const response=new Response(new ReadableStream({pull(controller){controller.enqueue(new Uint8Array(65536));},cancel(){cancelled=true;}}));
  assert.equal((await boundedResponse(response,131072)).length,131072);assert.equal(cancelled,true);
});
test("HTTP errors and unsupported seeks return unknown instead of a guessed quality",async()=>{
  assert.equal(await probeVideo("https://cdn.example/opaque",async()=>new Response("forbidden",{status:403})),null);
});
