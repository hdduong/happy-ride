import { SpanKind, type Attributes } from "@opentelemetry/api";
import type { ReadableSpan, SpanProcessor } from "@opentelemetry/sdk-trace-base";

const routes = new Set(["/", "/rider", "/driver"]);
const methods = new Set(["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"]);
const spanTypes = new Set([
  "BaseServer.handleRequest", "AppRender.fetch", "AppRender.getBodyResult",
  "Render.getServerSideProps", "Render.getStaticProps", "Render.renderDocument",
  "Node.runHandler", "AppRouteRouteHandlers.runHandler", "Middleware.execute",
  "ResolveMetadata.generateMetadata", "ResolveMetadata.generateViewport",
  "NextNodeServer.createComponentTree", "NextNodeServer.findPageComponents",
  "NextNodeServer.getLayoutOrPageModule", "NextNodeServer.startResponse",
  "NextNodeServer.clientComponentLoading",
]);

// Only registered application routes are exported. A missing route, a 404's
// arbitrary path, a query string, and a future unregistered route are unmatched.
// Add the route template here when adding a route to app/.
export function safeRoute(value: unknown): string {
  return typeof value === "string" && routes.has(value) ? value : "(unmatched)";
}

export function safeMethod(value: unknown): string {
  return typeof value === "string" && methods.has(value) ? value : "OTHER";
}

export function safeStatusCode(value: unknown): number | undefined {
  return typeof value === "number" && Number.isInteger(value) && value >= 100 && value <= 599
    ? value : undefined;
}

export type ServerRequestContext = {
  routePath?: unknown;
  routerKind?: unknown;
  routeType?: unknown;
  renderSource?: unknown;
  revalidateReason?: unknown;
};

export function safeRequestError(error: unknown, method: unknown, context: ServerRequestContext): Attributes {
  const value = error instanceof Error ? error : undefined;
  const knownErrors = new Set(["Error", "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "AggregateError"]);
  const errorType = value && knownErrors.has(value.name) ? value.name : "Error";
  const digest = value && "digest" in value && typeof value.digest === "string" && /^\d{1,24}$/.test(value.digest)
    ? value.digest : undefined;
  // Error messages and stack first lines can contain request input or secrets.
  // Keep only bounded frame positions, with known application file basenames.
  const files = new Set(["page.tsx", "layout.tsx", "route.ts", "page.js", "layout.js", "route.js", "server.js"]);
  const frames = (value?.stack ?? "").split("\n").slice(1, 21).flatMap((line) => {
    const frame = line.match(/([A-Za-z0-9_.-]+\.(?:[cm]?js|tsx?|jsx)):(\d{1,8}):(\d{1,8})\)?$/);
    return frame ? [`    at ${files.has(frame[1]) ? frame[1] : "server"}:${frame[2]}:${frame[3]}`] : [];
  }).slice(0, 10);
  const attributes: Attributes = {
    Route: safeRoute(context.routePath),
    Method: safeMethod(method),
    "exception.type": errorType,
    "exception.message": "Next.js server request failed.",
    "exception.stacktrace": `${errorType}: Next.js server request failed.\n${frames.join("\n")}`,
  };
  if (digest) attributes.ErrorDigest = digest;
  if (["App Router", "Pages Router"].includes(String(context.routerKind))) attributes.RouterKind = String(context.routerKind);
  if (["render", "route", "action", "middleware"].includes(String(context.routeType))) attributes.RouteType = String(context.routeType);
  if (["react-server-components", "react-server-components-payload", "server-rendering"].includes(String(context.renderSource))) attributes.RenderSource = String(context.renderSource);
  if (["on-demand", "stale"].includes(String(context.revalidateReason))) attributes.RevalidateReason = String(context.revalidateReason);
  return attributes;
}

// Azure Monitor 1.20.1 registers custom processors before its batch exporter.
// Next.js sets raw targets, fallback span names and exception events itself;
// sanitize the ended span before the exporter can serialize any of them.
export class SafeServerSpanProcessor implements SpanProcessor {
  constructor(private readonly requestCompleted: (span: ReadableSpan, attributes: Attributes) => void = () => {}) {}
  onStart(): void {}
  onEnd(span: ReadableSpan): void {
    const route = safeRoute(span.attributes["http.route"] ?? span.attributes["next.route"]);
    const method = safeMethod(span.attributes["http.request.method"] ?? span.attributes["http.method"]);
    const status = safeStatusCode(span.attributes["http.response.status_code"] ?? span.attributes["http.status_code"]);
    const type = typeof span.attributes["next.span_type"] === "string" && spanTypes.has(span.attributes["next.span_type"])
      ? span.attributes["next.span_type"] : "NextServer.operation";
    const attributes: Attributes = { "http.route": route, "next.route": route, "next.span_type": type };
    if (span.kind === SpanKind.SERVER || method !== "OTHER") attributes["http.method"] = method;
    if (status !== undefined) attributes["http.status_code"] = status;
    if (typeof span.attributes["next.rsc"] === "boolean") attributes["next.rsc"] = span.attributes["next.rsc"];
    const sampleRate = span.attributes["microsoft.sample_rate"];
    if (typeof sampleRate === "number" && sampleRate >= 0 && sampleRate <= 100) attributes["microsoft.sample_rate"] = sampleRate;
    for (const key of Object.keys(span.attributes)) delete span.attributes[key];
    Object.assign(span.attributes, attributes);
    // These are mutable SDK span fields, even though the exporter-facing
    // ReadableSpan interface is read-only (the Azure processor does the same).
    const writable = span as { name: string; status: { code: number } };
    writable.name = span.kind === SpanKind.SERVER ? `${method} ${route}` : `${type} ${route}`;
    writable.status = { code: span.status.code };
    span.events.length = 0; // onRequestError emits one sanitized exception.
    span.links.length = 0; // No unbounded link attributes or tracestate payloads.
    if (span.kind === SpanKind.SERVER) {
      this.requestCompleted(span, {
        Method: method, Route: route,
        ...(status === undefined ? {} : { StatusCode: status }),
        ElapsedMs: Math.max(0, Math.round((span.duration[0] * 1000 + span.duration[1] / 1e6) * 100) / 100),
      });
    }
  }
  async forceFlush(): Promise<void> {}
  async shutdown(): Promise<void> {}
}
