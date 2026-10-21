import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  reactStrictMode: true,
  devIndicators: false,
  // Keep the Node SDK external so Next's instrumentation and the SDK share the
  // process-wide OpenTelemetry provider, and trace it into standalone output.
  serverExternalPackages: [
    "@azure/monitor-opentelemetry", "@opentelemetry/api", "@opentelemetry/api-logs",
    "@opentelemetry/resources", "@opentelemetry/sdk-trace-base",
  ],
};

export default nextConfig;
