// Run after `npm run build`. Exercises the shipped standalone artifact and the real
// Azure SDK against loopback ingestion; no production route or cloud account.
import assert from "node:assert/strict";
import { fork } from "node:child_process";
import { once } from "node:events";
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync, inflateSync } from "node:zlib";

const web = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const canary = "PRIVATE_TELEMETRY_SMOKE_CANARY";
const dummyKey = "00000000-0000-0000-0000-000000000017";
const temporary = await mkdtemp(path.join(os.tmpdir(), "happyride-server-telemetry-"));
const app = path.join(temporary, "standalone");
const children = new Set();
const outputs = [];
const envelopes = [];
const collectorErrors = [];
let receivedBatches = 0;
let rejectedBatches = 0;
let rejectIngestion = false;
let sink;

// This fixture is outside the application. It drives the actual bundled Next
// instrumentation export over parent/child IPC, never an HTTP debug endpoint.
const bootstrap = String.raw`
const net = require("node:net");
const originalConnect = net.Socket.prototype.connect;
net.Socket.prototype.connect = function (...args) {
  const first = args[0];
  const options = Array.isArray(first) ? first[0] : first;
  const host = typeof options === "object" && options !== null
    ? options.host ?? options.hostname
    : typeof args[1] === "string" ? args[1] : undefined;
  if (host && !["localhost", "127.0.0.1", "::1", "[::1]"].includes(host)) {
    throw new Error("Smoke test blocked nonlocal network");
  }
  return originalConnect.apply(this, args);
};
// SDK OneSettings can run outside the exporter pipeline. The socket guard also
// covers that traffic and permits only the local collector/application.
require("./server.js");
const reusedError = new TypeError("PRIVATE_TELEMETRY_SMOKE_CANARY");
reusedError.stack = "TypeError: PRIVATE_TELEMETRY_SMOKE_CANARY\n    at privateAction (/private/PRIVATE_TELEMETRY_SMOKE_CANARY.ts:12:7)";
reusedError.digest = "123456789";
process.on("message", async ({ id, command }) => {
  try {
    const instrumentation = require("./.next/server/instrumentation.js");
    if (command === "register") {
      await Promise.all([instrumentation.register(), instrumentation.register(), instrumentation.register()]);
      process.send({ id, ok: true, enabled: globalThis[Symbol.for("happy-ride.web.server-telemetry")]?.enabled });
    } else if (command === "error") {
      const { trace, SpanKind, SpanStatusCode } = require("@opentelemetry/api");
      let traceId;
      await trace.getTracer("standalone-smoke").startActiveSpan("private span PRIVATE_TELEMETRY_SMOKE_CANARY", {
        kind: SpanKind.SERVER,
        attributes: { "http.method": "GET", "http.route": "/rider", "http.status_code": 500,
          "http.target": "/rider?private=PRIVATE_TELEMETRY_SMOKE_CANARY" },
      }, async (span) => {
        traceId = span.spanContext().traceId;
        const request = { method: "GET", path: "/rider?private=PRIVATE_TELEMETRY_SMOKE_CANARY",
          headers: { authorization: "PRIVATE_TELEMETRY_SMOKE_CANARY" } };
        const context = { routePath: "/rider", routeType: "render", routerKind: "App Router",
          renderSource: "react-server-components" };
        await instrumentation.onRequestError(reusedError, request, context);
        await instrumentation.onRequestError(reusedError, request, context);
        span.setStatus({ code: SpanStatusCode.ERROR, message: "PRIVATE_TELEMETRY_SMOKE_CANARY" });
        span.recordException(reusedError);
        span.end();
      });
      process.send({ id, ok: true, traceId });
    }
  } catch (error) { process.send({ id, ok: false, message: String(error) }); }
});
`;

async function until(check, description, timeout = 30000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const result = await check();
    if (result) return result;
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error(`Timed out: ${description}`);
}

async function availablePort() {
  const server = net.createServer();
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const port = server.address().port;
  await new Promise((resolve) => server.close(resolve));
  return port;
}

async function start(connectionString) {
  const port = await availablePort();
  const inherited = Object.fromEntries(Object.entries(process.env).filter(([name]) =>
    !/^(APPLICATIONINSIGHTS_|OTEL_|AZURE_MONITOR_|WEBSITE_|HTTP_PROXY$|HTTPS_PROXY$|ALL_PROXY$)/i.test(name)));
  const env = { ...inherited, NODE_ENV: "production", NEXT_RUNTIME: "nodejs", PORT: String(port),
    HOSTNAME: "127.0.0.1",
    NODE_USE_SYSTEM_CA: "1", WEBSITE_SITE_NAME: "app-grabbike-web", OTEL_SERVICE_NAME: "happy-ride-web",
    APPLICATIONINSIGHTS_STATSBEAT_DISABLED: "true", APPLICATIONINSIGHTS_SDKSTATS_DISABLED: "true",
    // Public SDK transport configuration, restricted to this child fixture.
    // Production retains HTTPS defaults; the socket guard forbids remote hosts.
    APPLICATIONINSIGHTS_CONFIGURATION_CONTENT: JSON.stringify({ azureMonitorExporterOptions: {
      // The connection-string parser normalizes endpoints to HTTPS. The SDK's
      // explicit host override keeps this test transport on the loopback sink.
      host: `http://127.0.0.1:${sink.address().port}/`,
      allowInsecureConnection: true, disableOfflineStorage: true,
    } }),
    NO_PROXY: "localhost,127.0.0.1,::1", no_proxy: "localhost,127.0.0.1,::1",
    OTEL_BSP_SCHEDULE_DELAY: "100", OTEL_BLRP_SCHEDULE_DELAY: "100", OTEL_METRIC_EXPORT_INTERVAL: "3600000",
    TEMP: path.join(temporary, "cache"), TMP: path.join(temporary, "cache"), TMPDIR: path.join(temporary, "cache") };
  delete env.NEXT_PHASE;
  delete env.NODE_OPTIONS;
  delete env.NODE_PATH;
  delete env.APPLICATIONINSIGHTS_CONNECTION_STRING;
  if (connectionString !== undefined) env.APPLICATIONINSIGHTS_CONNECTION_STRING = connectionString;
  const child = fork(path.join(app, "smoke-bootstrap.cjs"), [], {
    cwd: app, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe", "ipc"],
  });
  children.add(child);
  let output = "";
  outputs.push(() => output);
  child.stdout.on("data", (data) => { output += data; });
  child.stderr.on("data", (data) => { output += data; });
  const url = `http://127.0.0.1:${port}`;
  try {
    await until(async () => {
      if (child.exitCode !== null) throw new Error(`Standalone exited: ${output}`);
      try { return (await fetch(url, { signal: AbortSignal.timeout(1000) })).status === 200; }
      catch { return false; }
    }, "standalone startup");
  } catch (error) { throw new Error(`${error.message}\n${output}`); }
  let sequence = 0;
  return {
    child, url, output: () => output,
    command(command) {
      const id = ++sequence;
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { child.off("message", receive); reject(new Error(`IPC timed out: ${command}\n${output}`)); }, 15000);
        function receive(message) {
          if (message.id !== id) return;
          clearTimeout(timeout); child.off("message", receive);
          if (message.ok) resolve(message); else reject(new Error(message.message));
        }
        child.on("message", receive); child.send({ id, command });
      });
    },
  };
}

async function stop(child) {
  if (child.exitCode === null && child.signalCode === null) {
    const exited = once(child, "exit");
    child.kill();
    await exited;
  }
  children.delete(child);
}

function data(item) { return item.data?.baseData; }
function type(item) { return item.data?.baseType; }
function role(item) { return item.tags?.["ai.cloud.role"]; }

try {
  assert.ok(existsSync(path.join(web, ".next/standalone/server.js")), "Run npm run build first");
  await cp(path.join(web, ".next/standalone"), app, { recursive: true, dereference: true });
  await cp(path.join(web, ".next/static"), path.join(app, ".next/static"), { recursive: true });
  await mkdir(path.join(temporary, "cache"));
  await writeFile(path.join(app, "smoke-bootstrap.cjs"), bootstrap);
  sink = http.createServer(async (request, response) => {
    try {
      receivedBatches++;
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      let body = Buffer.concat(chunks);
      if (request.headers["content-encoding"] === "gzip") body = gunzipSync(body);
      if (request.headers["content-encoding"] === "deflate") body = inflateSync(body);
      const parsed = JSON.parse(body.toString());
      const batch = Array.isArray(parsed) ? parsed : [parsed];
      envelopes.push(...batch);
      if (rejectIngestion) {
        rejectedBatches++;
        response.writeHead(503, { "retry-after": "0" }); response.end();
      } else {
        response.writeHead(200, { "content-type": "application/json" });
        response.end(JSON.stringify({ itemsReceived: batch.length, itemsAccepted: batch.length, errors: [] }));
      }
    } catch (error) { collectorErrors.push({ error: String(error), encoding: request.headers["content-encoding"] }); response.writeHead(400); response.end(); }
  });
  sink.listen(0, "127.0.0.1");
  await once(sink, "listening");
  const connection = `InstrumentationKey=${dummyKey};IngestionEndpoint=http://127.0.0.1:${sink.address().port}/`;

  const server = await start(connection);
  assert.equal((await server.command("register")).enabled, true);
  await until(() => envelopes.some((item) => type(item) === "MessageData" && data(item).message === "WebServerStarted"), "startup application log");
  const beforeRider = envelopes.length;
  assert.equal((await fetch(`${server.url}/rider?as=${canary}`, { headers: {
    authorization: `Bearer ${canary}`, cookie: `private=${canary}`, "x-private-canary": canary,
  } })).status, 200);
  const realRequest = await until(() => envelopes.slice(beforeRider).find((item) => type(item) === "RequestData" && data(item).name === "GET /rider"), "real rider request export");
  assert.equal(role(realRequest), "happy-ride-web");
  assert.equal(data(realRequest).responseCode, "200");
  assert.match(realRequest.tags["ai.operation.id"], /^[a-f\d]{32}$/i);
  assert.equal((await fetch(`${server.url}/${canary}?private=${canary}`)).status, 404);
  await until(() => envelopes.some((item) => type(item) === "RequestData" && data(item).name === "GET (unmatched)"), "sanitized unmatched route");

  const error1 = await server.command("error");
  await until(() => envelopes.some((item) => type(item) === "ExceptionData" && item.tags["ai.operation.id"] === error1.traceId), "first bundled request-error export");
  const error2 = await server.command("error");
  await until(() => envelopes.some((item) => type(item) === "ExceptionData" && item.tags["ai.operation.id"] === error2.traceId), "reused error in another request");
  assert.notEqual(error1.traceId, error2.traceId);
  await new Promise((resolve) => setTimeout(resolve, 350));
  for (const traceId of [error1.traceId, error2.traceId]) {
    const exceptions = envelopes.filter((item) => type(item) === "ExceptionData" && item.tags["ai.operation.id"] === traceId);
    assert.equal(exceptions.length, 1, "one error export per request, despite duplicate hook delivery and span exception");
    assert.equal(role(exceptions[0]), "happy-ride-web");
    assert.equal(data(exceptions[0]).exceptions[0].typeName, "TypeError");
  }
  assert.equal(envelopes.filter((item) => type(item) === "MessageData" && data(item).message === "WebServerStarted").length, 1);
  assert.equal(envelopes.filter((item) => type(item) === "RequestData" && item.tags["ai.operation.id"] === realRequest.tags["ai.operation.id"]).length, 1);
  assert.ok(envelopes.some((item) => type(item) === "MessageData" && data(item).message === "WebRequestCompleted"));
  assert.equal(JSON.stringify(envelopes).includes(canary), false, "no URL, query, header, exception message, or raw stack canary is exported");
  assert.ok(envelopes.every((item) => role(item) === "happy-ride-web"), "App Service detector must not replace the application role");
  console.log("PASS standalone requests, logs, correlated exceptions, role, privacy, and initialization/error deduplication");

  rejectIngestion = true;
  assert.equal((await fetch(`${server.url}/driver?private=${canary}`)).status, 200);
  await until(() => rejectedBatches > 0, "exporter rejected by collector");
  assert.equal((await fetch(`${server.url}/rider`)).status, 200, "exporter failure cannot break rendering");
  await stop(server.child);
  rejectIngestion = false;
  console.log("PASS exporter failures leave pages healthy");

  for (const [name, value] of [["missing", undefined], ["malformed", "InstrumentationKey=not-a-guid"]]) {
    const before = envelopes.length;
    const disabled = await start(value);
    assert.equal((await fetch(`${disabled.url}/rider`)).status, 200);
    await disabled.command("register");
    await new Promise((resolve) => setTimeout(resolve, 250));
    assert.equal(envelopes.length, before, `${name} configuration must not export`);
    await stop(disabled.child);
    console.log(`PASS ${name} connection string keeps standalone startup and rendering healthy`);
  }

  let assets = 0;
  async function inspectAssets(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) await inspectAssets(file);
      else if (/\.(js|json|html)$/.test(entry.name)) {
        const content = await readFile(file, "utf8"); assets++;
        for (const privateValue of [dummyKey, "build-privacy-canary.invalid", connection, "useAzureMonitor", "AzureMonitorTraceExporter", "@azure/monitor-opentelemetry"])
          assert.equal(content.includes(privateValue), false, `server-only SDK/configuration appeared in ${entry.name}`);
      }
    }
  }
  await inspectAssets(path.join(app, ".next/static"));
  console.log(`PASS ${assets} browser assets contain no server SDK or connection-string canary`);
  console.log(JSON.stringify({ passed: 5, requests: envelopes.filter((item) => type(item) === "RequestData").length,
    exceptions: envelopes.filter((item) => type(item) === "ExceptionData").length, browserAssets: assets }));
} catch (error) {
  for (const output of outputs) console.error(output().slice(-6000));
  console.error("Captured envelope types:", envelopes.map(type));
  console.error("Collector diagnostics:", { receivedBatches, collectorErrors });
  throw error;
} finally {
  for (const child of children) await stop(child);
  if (sink) { sink.closeAllConnections(); await new Promise((resolve) => sink.close(resolve)); }
  const resolved = path.resolve(temporary);
  assert.ok(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep)
    && path.basename(resolved).startsWith("happyride-server-telemetry-"), "Refusing cleanup outside task temporary folder");
  await rm(resolved, { recursive: true, force: true });
}
