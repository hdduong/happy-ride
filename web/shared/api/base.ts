function configuredApiOrigin(): string {
  const configured = process.env.API_BASE_URL?.trim() || process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  const base = configured && configured.length > 0 ? configured : "http://localhost:5080";
  return base.replace(/\/+$/, "");
}

export function apiBase(): string {
  // Ordinary API calls stay on this Next.js server so it can attach the Entra token.
  if (typeof window !== "undefined") return "/backend";
  return configuredApiOrigin();
}

export function realtimeBase(): string {
  // SignalR uses a WebSocket, which this Next.js server cannot carry.
  // The hub URL is the API itself; the browser asks this server for a token first.
  return configuredApiOrigin();
}
