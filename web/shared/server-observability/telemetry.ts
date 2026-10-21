import { context, isSpanContextValid, trace, type Attributes, type Context } from "@opentelemetry/api";
import { logs, SeverityNumber } from "@opentelemetry/api-logs";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { SafeServerSpanProcessor, safeRequestError, type ServerRequestContext } from "./policy";

type TelemetryState = { registration?: Promise<boolean>; enabled: boolean; errors: WeakMap<object, Set<string>> };
const stateKey = Symbol.for("happy-ride.web.server-telemetry");
const globals = globalThis as typeof globalThis & { [stateKey]?: TelemetryState };
const state = globals[stateKey] ??= { enabled: false, errors: new WeakMap<object, Set<string>>() };
type ServerEvent = "WebServerStarted" | "WebRequestCompleted" | "WebRequestError";

function writeEvent(eventName: ServerEvent, attributes: Attributes = {}, eventContext: Context = context.active()): void {
  // Callers supply only policy-filtered attributes. Console is deliberately not
  // auto-instrumented: framework errors can include raw URLs and request input.
  const spanContext = trace.getSpanContext(eventContext);
  const fields = { EventName: eventName, ...attributes, ...(spanContext ? { TraceId: spanContext.traceId } : {}) };
  const exception = eventName === "WebRequestError";
  try {
    (exception ? console.error : console.info)(JSON.stringify(fields));
    if (state.enabled) logs.getLogger("happy-ride-web").emit({
      body: eventName,
      severityNumber: exception ? SeverityNumber.ERROR : SeverityNumber.INFO,
      severityText: exception ? "ERROR" : "INFO",
      attributes: fields,
      context: eventContext,
    });
  } catch { /* Telemetry cannot turn a rendered page into a request failure. */ }
}

export function registerServerTelemetry(): Promise<boolean> {
  if (process.env.NEXT_PHASE === "phase-production-build") return Promise.resolve(false);
  state.registration ??= (async () => {
    const connectionString = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING?.trim();
    if (!connectionString) return false;
    try {
      const { useAzureMonitor } = await import("@azure/monitor-opentelemetry");
      useAzureMonitor({
        azureMonitorExporterOptions: { connectionString },
        resource: resourceFromAttributes({ "service.name": "happy-ride-web" }),
        samplingRatio: 1,
        tracesPerSecond: 0,
        enableTraceBasedSamplingForLogs: false,
        enableLiveMetrics: false,
        enableStandardMetrics: false,
        enablePerformanceCounters: false,
        browserSdkLoaderOptions: { enabled: false },
        instrumentationOptions: {
          http: { enabled: false }, azureSdk: { enabled: false },
          mongoDb: { enabled: false }, mySql: { enabled: false }, postgreSql: { enabled: false },
          redis: { enabled: false }, redis4: { enabled: false },
          bunyan: { enabled: false }, winston: { enabled: false }, console: { enabled: false },
        },
        spanProcessors: [new SafeServerSpanProcessor((span, attributes) => {
          writeEvent("WebRequestCompleted", attributes, trace.setSpanContext(context.active(), span.spanContext()));
        })],
      });
      state.enabled = true;
      writeEvent("WebServerStarted");
      return true;
    } catch {
      console.warn(JSON.stringify({ EventName: "WebTelemetryUnavailable" }));
      return false;
    }
  })();
  return state.registration;
}

export function recordServerRequestError(error: unknown, request: { method: unknown }, requestContext: ServerRequestContext): void {
  const eventContext = context.active();
  const spanContext = trace.getSpanContext(eventContext);
  if (error !== null && typeof error === "object" && spanContext && isSpanContextValid(spanContext)) {
    // Next can report one error more than once within a request. Cached Error
    // objects can also be reused by later requests, which each need their own
    // exception and trace correlation. Keep the history bounded per object.
    const traces = state.errors.get(error) ?? new Set<string>();
    if (traces.has(spanContext.traceId)) return;
    if (traces.size >= 128) traces.delete(traces.values().next().value!);
    traces.add(spanContext.traceId);
    state.errors.set(error, traces);
  }
  writeEvent("WebRequestError", safeRequestError(error, request.method, requestContext), eventContext);
}
