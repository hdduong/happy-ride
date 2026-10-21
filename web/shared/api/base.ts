export function apiBase(): string {
  // The browser talks to this Next.js server. The server attaches the Entra token.
  if (typeof window !== "undefined") return "/backend";
  const configured = process.env.API_BASE_URL?.trim() || process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  const base = configured && configured.length > 0 ? configured : "http://localhost:5080";
  return base.replace(/\/+$/, "");
}
