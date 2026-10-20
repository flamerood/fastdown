import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import assert from "node:assert/strict";
const server=createServer(async(req,res)=>{
  console.log("REQUEST",req.method,req.url);
  if(req.url==="/page") {res.setHeader("Content-Type","text/html");res.end('<!doctype html><title>Local video</title><video controls><source src="/manifest.mpd" type="application/dash+xml"></video>');return;}
  if(req.url==="/manifest.mpd") {res.setHeader("Content-Type","application/dash+xml");res.end('<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT2S" minBufferTime="PT1S"><Period><AdaptationSet mimeType="video/mp4" contentType="video"><Representation id="720" width="1280" height="720" codecs="avc1.42C01F" bandwidth="100000"><BaseURL>split-video-720.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet mimeType="audio/mp4" contentType="audio"><Representation id="audio" codecs="mp4a.40.2" bandwidth="64000"><BaseURL>split-audio.m4a</BaseURL></Representation></AdaptationSet></Period></MPD>');return;}
  if(["/split-video-720.mp4","/split-audio.m4a"].includes(req.url)) {const bytes=await readFile(resolve("tests/browser/fixtures/"+req.url.slice(1)));res.setHeader("Content-Type",req.url.endsWith("m4a")?"audio/mp4":"video/mp4");res.end(bytes);return;}
  res.writeHead(404);res.end();
});
await new Promise(r=>server.listen(0,"127.0.0.1",r));
let result;
if(process.argv.includes("--native")) {
  const fixture=spawn("dotnet",[resolve("tests/Fastdown.Tests/bin/Release/net10.0/Fastdown.Tests.dll"),"--browser-fixture"],{stdio:["pipe","pipe","inherit"],windowsHide:true});
  const lines=createInterface({input:fixture.stdout});await new Promise(r=>lines.once("line",r));
  const host=spawn(resolve("Fastdown/Fastdown.BrowserHost.exe"),["chrome-extension://cjfejnimgnbehgbgjejbkkdifjoakfnd/"],{stdio:["pipe","pipe","inherit"],windowsHide:true});
  const data=Buffer.from(JSON.stringify({action:"inspect-video",url:`http://127.0.0.1:${server.address().port}/page`})),header=Buffer.alloc(4);header.writeInt32LE(data.length);host.stdin.end(Buffer.concat([header,data]));
  const chunks=[];host.stdout.on("data",d=>chunks.push(d));await new Promise(r=>host.once("exit",r));
  const reply=JSON.parse(Buffer.concat(chunks).subarray(4).toString());
  assert.equal(reply.ok,true,reply.error);assert.deepEqual(reply.video.heights,[720]);console.log("PASS Native page extraction with an inherited browser input pipe");
  fixture.stdin.end("stop\n");await new Promise(r=>fixture.once("exit",r));lines.close();result=0;
} else {
  const child=spawn("dotnet",[resolve("tests/Fastdown.Tests/bin/Release/net10.0/Fastdown.Tests.dll"),"--video-smoke",`http://127.0.0.1:${server.address().port}/page`,"720"],{stdio:"inherit",windowsHide:true});
  result=await new Promise(r=>child.on("exit",r));
}
server.closeAllConnections();await new Promise(r=>server.close(r));process.exitCode=result||0;
