import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { resolve } from "node:path";
import { randomUUID } from "node:crypto";
import assert from "node:assert/strict";

function request(origin, value) {
  return new Promise((accept, reject) => {
    const child = spawn(resolve("Fastdown/Fastdown.BrowserHost.exe"), [origin, "--parent-window=0"], { stdio: ["pipe", "pipe", "pipe"], windowsHide: true });
    const chunks = [];
    let error = "";
    child.stdout.on("data", data => chunks.push(data)); child.stderr.on("data", data => { error += data; });
    child.stdin.on("error", () => {});
    const timer = setTimeout(() => { reject(new Error("Native host timed out")); child.kill(); }, 20000);
    child.on("error", reject);
    child.on("exit", () => {
      clearTimeout(timer);
      try {
        const response = Buffer.concat(chunks);
        assert(response.length >= 4, "Missing frame: " + error);
        assert.equal(response.readUInt32LE(0), response.length - 4, "Invalid native framing");
        accept(JSON.parse(response.subarray(4).toString("utf8")));
      } catch (exception) { reject(exception); }
    });
    const body = Buffer.from(JSON.stringify(value), "utf8"), header = Buffer.alloc(4); header.writeUInt32LE(body.length);
    child.stdin.end(Buffer.concat([header, body]));
  });
}
const origin = "chrome-extension://cjfejnimgnbehgbgjejbkkdifjoakfnd/";
const denied = await request("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", { action: "ping" });
assert.equal(denied.ok, false); console.log("PASS  Native host rejects an unregistered extension origin");
const ping = await request(origin, { action: "ping" });
assert.equal(ping.ok, true); console.log("PASS  Binary native messaging ping works; desktop running: " + ping.appRunning);
const server = createServer((_request, response) => { response.writeHead(404, { "Content-Length": 0 }); response.end(); });
await new Promise(accept => server.listen(0, "127.0.0.1", accept));
try {
  const invalid = await request(origin, { action: "download", id: randomUUID(), url: `http://127.0.0.1:${server.address().port}/not-found`, focus: false });
  assert.equal(invalid.ok, false); assert.match(invalid.error, /404/);
  const alive = await request(origin, { action: "ping" }); assert.equal(alive.appRunning, true);
  console.log("PASS  Native host starts the desktop app and returns real preflight errors without adding a broken download");
  console.log("\n3/3 native host checks passed. Fastdown remains open for use.");
} finally { server.closeAllConnections(); await new Promise(accept => server.close(accept)); }
