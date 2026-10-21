export function apiBase(): string {
  const configured = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  const base = configured && configured.length > 0 ? configured : "http://localhost:5080";
  return base.replace(/\/+$/, "");
}
