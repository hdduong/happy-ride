import type { Instrumentation } from "next";

export async function register(): Promise<void> {
  if (process.env.NEXT_RUNTIME === "nodejs" && process.env.NEXT_PHASE !== "phase-production-build") {
    try {
      const { registerServerTelemetry } = await import("./shared/server-observability/telemetry");
      await registerServerTelemetry();
    } catch {
      console.warn(JSON.stringify({ EventName: "WebTelemetryUnavailable" }));
    }
  }
}

export const onRequestError: Instrumentation.onRequestError = async (error, request, context) => {
  if (process.env.NEXT_RUNTIME === "nodejs" && process.env.NEXT_PHASE !== "phase-production-build") {
    try {
      const { recordServerRequestError } = await import("./shared/server-observability/telemetry");
      recordServerRequestError(error, request, context);
    } catch { /* Error reporting must not change Next.js error handling. */ }
  }
};
