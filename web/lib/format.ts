export function formatKm(distanceM: number): string {
  const km = distanceM / 1000;
  if (!Number.isFinite(km)) return "0 km";
  if (km >= 10) return `${Math.round(km)} km`;
  const text = km.toFixed(1);
  return `${text.endsWith(".0") ? text.slice(0, -2) : text} km`;
}

export function formatMinutes(durationS: number): string {
  return `${minutesFromSeconds(durationS)} min`;
}

export function minutesFromSeconds(durationS: number): number {
  if (!Number.isFinite(durationS)) return 1;
  return Math.max(1, Math.round(durationS / 60));
}

export function formatVnd(amount: number): string {
  const value = Number.isFinite(amount) ? Math.round(amount) : 0;
  return `${value.toLocaleString("en-US")} VND`;
}

export function formatEta(meters: number, speedKmh: number): string {
  const speed = speedKmh >= 5 ? speedKmh : 20;
  const minutes = (meters / 1000 / speed) * 60;
  if (!Number.isFinite(minutes)) return "1 min";
  return `${Math.max(1, Math.round(minutes))} min`;
}

export function shortPlace(address: string): string {
  const trimmed = address.trim();
  if (!trimmed) return "";
  return trimmed.split(",")[0]?.trim() || trimmed;
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const first = parts[0]?.[0] ?? "";
  const second = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? "") : (parts[0]?.[1] ?? "");
  return `${first}${second}`.toUpperCase();
}

export function expiryMs(value: string): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}
