import assert from "node:assert/strict";
import test from "node:test";
import { context, ROOT_CONTEXT, SpanKind, SpanStatusCode, trace } from "@opentelemetry/api";
import { BasicTracerProvider, InMemorySpanExporter, SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base";
import { SafeServerSpanProcessor, safeRequestError, safeRoute } from "../shared/server-observability/policy.ts";

const privateValue = "PRIVATE_ADDRESS_AND_USERNAME";
function span(overrides = {}) {
  return {
    name: `GET /unknown/${privateValue}?as=${privateValue}`,
    kind: SpanKind.SERVER,
    attributes: {
      "http.method": "GET", "http.status_code": 404,
      "http.target": `/unknown/${privateValue}?as=${privateValue}`,
      "http.url": `https://example.com/${privateValue}`, "url.full": privateValue,
      "url.query": privateValue, "url.path": privateValue,
      "next.span_name": privateValue, "next.span_type": "BaseServer.handleRequest",
      "http.request.header.cookie": privateValue, "arbitrary": privateValue,
    },
    status: { code: SpanStatusCode.ERROR, message: privateValue },
    events: [{ name: "exception", attributes: { "exception.message": privateValue, "exception.stacktrace": privateValue } }],
    links: [{ attributes: { private: privateValue } }],
    duration: [0, 123456789],
    spanContext: () => ({ traceId: "a".repeat(32), spanId: "b".repeat(16), traceFlags: 1 }),
    ...overrides,
  };
}

test("server spans export only safe routes and fields while retaining status, duration and trace identity", () => {
  const requests = [];
  const processor = new SafeServerSpanProcessor((s, attributes) => requests.push({ span: s, attributes }));
  const request = span();
  const originalContext = request.spanContext();
  processor.onEnd(request);
  assert.equal(request.name, "GET (unmatched)");
  assert.deepEqual(request.attributes, {
    "http.method": "GET", "http.status_code": 404, "http.route": "(unmatched)",
    "next.route": "(unmatched)", "next.span_type": "BaseServer.handleRequest",
  });
  assert.deepEqual(request.status, { code: SpanStatusCode.ERROR });
  assert.deepEqual(request.events, []);
  assert.deepEqual(request.links, []);
  assert.deepEqual(request.spanContext(), originalContext);
  assert.deepEqual(requests[0].attributes, { Method: "GET", Route: "(unmatched)", StatusCode: 404, ElapsedMs: 123.46 });
  assert.equal(JSON.stringify(request).includes(privateValue), false);
});

test("known rider and driver routes preserve request names, while arbitrary route data is unmatched", () => {
  const processor = new SafeServerSpanProcessor();
  for (const route of ["/", "/rider", "/driver"]) {
    const request = span({ attributes: { "http.route": route, "http.request.method": "POST", "http.response.status_code": 200 } });
    processor.onEnd(request);
    assert.equal(request.name, `POST ${route}`);
    assert.equal(request.attributes["http.status_code"], 200);
  }
  for (const route of [`/rider?as=${privateValue}`, `/rider/${privateValue}`, privateValue, undefined]) {
    assert.equal(safeRoute(route), "(unmatched)");
  }
});

test("nested Next spans cannot expose URLs in names, span types, or exception events", () => {
  let completed = 0;
  const processor = new SafeServerSpanProcessor(() => completed++);
  const child = span({ kind: SpanKind.INTERNAL, attributes: { "next.route": "/driver", "next.span_type": privateValue } });
  processor.onEnd(child);
  assert.equal(child.name, "NextServer.operation /driver");
  assert.equal(completed, 0, "nested spans do not create duplicate request completion logs");
  assert.equal(JSON.stringify(child).includes(privateValue), false);
});

test("the real OpenTelemetry span is sanitized before its exporter serializes it", async () => {
  const exporter = new InMemorySpanExporter();
  const provider = new BasicTracerProvider({ spanProcessors: [
    new SafeServerSpanProcessor(), new SimpleSpanProcessor(exporter),
  ] });
  try {
    const request = provider.getTracer("real-server-test").startSpan(`GET /${privateValue}`, {
      kind: SpanKind.SERVER,
      attributes: { "http.method": "GET", "http.route": "/rider", "http.target": `/?as=${privateValue}` },
    });
    request.setStatus({ code: SpanStatusCode.ERROR, message: privateValue });
    request.recordException(new Error(privateValue));
    request.end();
    await provider.forceFlush();
    const [exported] = exporter.getFinishedSpans();
    assert.equal(exported.name, "GET /rider");
    assert.deepEqual(exported.status, { code: SpanStatusCode.ERROR });
    assert.equal(exported.events.length, 0);
    assert.equal(exported.attributes["http.target"], undefined);
    assert.equal(JSON.stringify({ name: exported.name, attributes: exported.attributes, status: exported.status, events: exported.events }).includes(privateValue), false);
  } finally { await provider.shutdown(); }
});

test("request errors retain a safe diagnostic type, digest, route and positions without raw messages or stacks", () => {
  const error = new TypeError(privateValue);
  error.digest = "123456789";
  error.stack = `TypeError: ${privateValue}\n    at action (C:\\Users\\${privateValue}\\app\\page.tsx:12:8)\n    at ${privateValue} (https://host/${privateValue}.js:30:2)`;
  const attributes = safeRequestError(error, "POST", {
    routePath: "/rider", routeType: "render", routerKind: "App Router", renderSource: "server-rendering", revalidateReason: "stale",
  });
  assert.equal(attributes["exception.type"], "TypeError");
  assert.equal(attributes.ErrorDigest, "123456789");
  assert.equal(attributes.Route, "/rider");
  assert.match(attributes["exception.stacktrace"], /page\.tsx:12:8/);
  assert.equal(JSON.stringify(attributes).includes(privateValue), false);
});

test("unknown error shapes and untrusted context cannot add payloads to telemetry", () => {
  const error = new Error(privateValue);
  error.name = privateValue;
  error.digest = `NEXT_REDIRECT;/rider?secret=${privateValue}`;
  const attributes = safeRequestError(error, privateValue, {
    routePath: privateValue, routeType: privateValue, routerKind: privateValue,
  });
  assert.equal(attributes["exception.type"], "Error");
  assert.equal(attributes.Method, "OTHER");
  assert.equal(attributes.Route, "(unmatched)");
  assert.equal(attributes.ErrorDigest, undefined);
  assert.equal(attributes.RouteType, undefined);
  assert.equal(JSON.stringify(attributes).includes(privateValue), false);
  assert.equal(safeRequestError("raw thrown secret", "GET", {}).Route, "(unmatched)");
});

test("instrumentation is inert in Edge and absent-configuration Node processes", async () => {
  const originalRuntime = process.env.NEXT_RUNTIME;
  const originalConnection = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
  const originalPhase = process.env.NEXT_PHASE;
  try {
    delete process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
    const { register, onRequestError } = await import("../instrumentation.ts");
    process.env.NEXT_RUNTIME = "edge";
    await register();
    await onRequestError(new Error(privateValue), { path: privateValue, method: "GET", headers: {} }, {});
    assert.equal(globalThis[Symbol.for("happy-ride.web.server-telemetry")], undefined, "Edge never imports server telemetry");
    process.env.NEXT_RUNTIME = "nodejs";
    process.env.NEXT_PHASE = "phase-production-build";
    process.env.APPLICATIONINSIGHTS_CONNECTION_STRING = "a-build-must-not-use-this-connection-string";
    await register();
    await onRequestError(new Error(privateValue), { path: privateValue, method: "GET", headers: {} }, {});
    assert.equal(globalThis[Symbol.for("happy-ride.web.server-telemetry")], undefined, "production builds never import or start telemetry");
    delete process.env.NEXT_PHASE;
    delete process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
    await register();
    const { registerServerTelemetry } = await import("../shared/server-observability/telemetry.ts");
    assert.equal(await registerServerTelemetry(), false);
    assert.equal(registerServerTelemetry(), registerServerTelemetry(), "one initialization promise per Node process");
    assert.equal(globalThis[Symbol.for("happy-ride.web.server-telemetry")].enabled, false);
  } finally {
    if (originalRuntime === undefined) delete process.env.NEXT_RUNTIME; else process.env.NEXT_RUNTIME = originalRuntime;
    if (originalConnection === undefined) delete process.env.APPLICATIONINSIGHTS_CONNECTION_STRING; else process.env.APPLICATIONINSIGHTS_CONNECTION_STRING = originalConnection;
    if (originalPhase === undefined) delete process.env.NEXT_PHASE; else process.env.NEXT_PHASE = originalPhase;
  }
});

test("cached errors are logged for each trace while duplicate hooks within a trace are suppressed", async () => {
  const { recordServerRequestError } = await import("../shared/server-observability/telemetry.ts");
  const records = [];
  const originalError = console.error;
  const originalActive = context.active;
  const firstTrace = "c".repeat(32);
  const secondTrace = "d".repeat(32);
  console.error = (record) => records.push(JSON.parse(record));
  try {
    const error = new Error(privateValue);
    const request = { method: "GET", path: privateValue, headers: { cookie: privateValue } };
    context.active = () => trace.setSpanContext(ROOT_CONTEXT, { traceId: firstTrace, spanId: "e".repeat(16), traceFlags: 1 });
    recordServerRequestError(error, request, { routePath: "/driver", routeType: "render" });
    recordServerRequestError(error, request, { routePath: "/driver", routeType: "render" });
    context.active = () => trace.setSpanContext(ROOT_CONTEXT, { traceId: secondTrace, spanId: "f".repeat(16), traceFlags: 1 });
    recordServerRequestError(error, request, { routePath: "/driver", routeType: "render" });
    recordServerRequestError(error, request, { routePath: "/driver", routeType: "render" });
    assert.equal(records.length, 2);
    assert.deepEqual(records.map((record) => record.TraceId), [firstTrace, secondTrace]);
    assert.equal(records[0].EventName, "WebRequestError");
    assert.equal(records[0].Route, "/driver");
    assert.equal(JSON.stringify(records).includes(privateValue), false);
  } finally { console.error = originalError; context.active = originalActive; }
});

test("errors without valid active trace context are never suppressed", async () => {
  const { recordServerRequestError } = await import("../shared/server-observability/telemetry.ts");
  const records = [];
  const originalError = console.error;
  const originalActive = context.active;
  console.error = (record) => records.push(JSON.parse(record));
  try {
    const error = new Error(privateValue);
    for (const eventContext of [ROOT_CONTEXT, trace.setSpanContext(ROOT_CONTEXT, { traceId: "0".repeat(32), spanId: "0".repeat(16), traceFlags: 0 })]) {
      context.active = () => eventContext;
      recordServerRequestError(error, { method: "GET" }, { routePath: "/rider" });
      recordServerRequestError(error, { method: "GET" }, { routePath: "/rider" });
    }
    assert.equal(records.length, 4);
  } finally { console.error = originalError; context.active = originalActive; }
});
