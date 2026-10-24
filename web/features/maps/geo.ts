import type { GeoLine, LatLng } from "./types";

export const DEFAULT_PICKUP = {
  name: "Chợ Bến Thành",
  address: "Chợ Bến Thành, District 1",
  lat: 10.77254,
  lng: 106.698,
};

export const HANOI_PICKUP = {
  name: "Hồ Hoàn Kiếm",
  address: "Hồ Hoàn Kiếm, Hanoi",
  lat: 21.0287,
  lng: 105.8524,
};

export const HANOI_CENTER: LatLng = { lat: HANOI_PICKUP.lat, lng: HANOI_PICKUP.lng };

export function isHanoi(city: string | undefined): boolean {
  return Boolean(city && /hanoi|ha noi|hà nội/i.test(city));
}

export const HANOI_RIDER01_ID = "77777777-7777-7777-7777-777777777777";

export const HANOI_RIDER01_PICKUP = {
  name: "Lẩu Ếch Hà My",
  address: "70 Ngõ 36 Giang Văn Minh, Phường Ba Đình, Hà Nội",
  lat: 21.03342,
  lng: 105.82845,
};

export function pickupForCity(city: string | undefined) {
  return isHanoi(city) ? HANOI_PICKUP : DEFAULT_PICKUP;
}

export function pickupForPersona(userId: string | undefined, city: string | undefined) {
  if (userId === HANOI_RIDER01_ID) return HANOI_RIDER01_PICKUP;
  return pickupForCity(city);
}

export function centerForCity(city: string | undefined): LatLng {
  const pickup = pickupForCity(city);
  return { lat: pickup.lat, lng: pickup.lng };
}

export function isPlotted(point: LatLng | null | undefined): point is LatLng {
  return Boolean(
    point &&
      Number.isFinite(point.lat) &&
      Number.isFinite(point.lng) &&
      !(point.lat === 0 && point.lng === 0),
  );
}

export function routeForStatus(
  status: string | undefined,
  approach: GeoLine | null | undefined,
  trip: GeoLine | null | undefined,
  fallback: LatLng[],
  progress?: LatLng | null,
  rideId?: string | null,
): LatLng[] {
  let line: LatLng[];
  if (status === "Accepted") {
    const approachLine = linePoints(approach);
    line = approachLine.length >= 2 ? approachLine : linePoints(trip);
  } else {
    line = linePoints(trip);
  }
  if (line.length < 2) line = fallback;
  if (!isPlotted(progress)) return line;
  return remainingLine(line, progress, 60, rideId);
}

const reachedAlongM = new Map<string, number>();
const backwardSlackM = 15;
const sameRoadM = 12;
let activeProgressRide: string | null = null;

// Drop the road already driven. Progress only moves forward, and a nearby later
// leg does not replace the leg the bike is still on. A sample behind the bike or
// off the road keeps the furthest cut. A new ride starts over.
export function remainingLine(points: LatLng[], current: LatLng, maxDistanceM = 60, rideId?: string | null): LatLng[] {
  if (points.length < 2 || !isPlotted(current)) return points;
  const rideKey = rideId && rideId.length > 0 ? rideId : "preview";
  if (activeProgressRide !== rideKey) {
    reachedAlongM.clear();
    activeProgressRide = rideKey;
  }
  const totals = [0];
  for (let i = 1; i < points.length; i++) totals.push(totals[i - 1] + haversineMeters(points[i - 1], points[i]));
  const key = routeProgressKey(points);
  const end = points[points.length - 1];
  if (haversineMeters(current, end) < 1) {
    reachedAlongM.set(key, totals[totals.length - 1]);
    return [];
  }
  const previous = reachedAlongM.get(key) ?? 0;
  const minAlong = Math.max(0, previous - backwardSlackM);
  const candidates: { index: number; point: LatLng; distanceM: number; alongM: number }[] = [];
  for (let i = 0; i < points.length - 1; i++) {
    const projected = closestOnSegment(points[i], points[i + 1], current);
    if (projected.distanceM > maxDistanceM) continue;
    const alongM = totals[i] + projected.alongSegmentM;
    if (alongM + 0.5 < minAlong) continue;
    candidates.push({ index: i, point: projected.point, distanceM: projected.distanceM, alongM });
  }
  if (candidates.length === 0) return previous > 0 ? cutAt(points, totals, previous) : points;
  const nearest = Math.min(...candidates.map((candidate) => candidate.distanceM));
  const chosen = candidates
    .filter((candidate) => candidate.distanceM <= nearest + sameRoadM)
    .reduce((earliest, candidate) => (candidate.alongM < earliest.alongM ? candidate : earliest));
  const recorded = Math.max(previous, chosen.alongM);
  reachedAlongM.set(key, recorded);
  if (chosen.alongM >= recorded) return cutAhead(points, chosen.index, chosen.point);
  return cutAt(points, totals, recorded);
}

function cutAt(points: LatLng[], totals: number[], alongM: number): LatLng[] {
  const held = pointAlong(points, totals, alongM);
  return cutAhead(points, held.index, held.point);
}

function pointAlong(points: LatLng[], totals: number[], alongM: number): { index: number; point: LatLng } {
  const last = points.length - 1;
  if (alongM >= totals[last]) return { index: last - 1, point: points[last] };
  let index = 0;
  while (index < last - 1 && totals[index + 1] < alongM) index += 1;
  const span = totals[index + 1] - totals[index];
  const t = span < 0.5 ? 0 : (alongM - totals[index]) / span;
  const start = points[index];
  const next = points[index + 1];
  return {
    index,
    point: {
      lat: start.lat + (next.lat - start.lat) * t,
      lng: start.lng + (next.lng - start.lng) * t,
    },
  };
}

function cutAhead(points: LatLng[], index: number, snap: LatLng): LatLng[] {
  const ahead = points.slice(index + 1);
  if (ahead.length === 0) return [];
  if (haversineMeters(snap, ahead[0]) < 1) return ahead.length >= 2 ? ahead : [];
  return [snap, ...ahead];
}

function routeProgressKey(points: LatLng[]): string {
  const first = points[0];
  const middle = points[Math.floor(points.length / 2)];
  const last = points[points.length - 1];
  return [points.length, first.lat, first.lng, middle.lat, middle.lng, last.lat, last.lng]
    .map((value) => (typeof value === "number" ? value.toFixed(5) : String(value)))
    .join(":");
}

function closestOnSegment(start: LatLng, end: LatLng, point: LatLng): { point: LatLng; distanceM: number; alongSegmentM: number } {
  const lengthM = haversineMeters(start, end);
  if (lengthM < 0.5) return { point: start, distanceM: haversineMeters(start, point), alongSegmentM: 0 };
  const meanLat = toRadians((start.lat + end.lat) / 2);
  const east = (lng: number) => toRadians(lng) * Math.cos(meanLat) * 6_371_000;
  const north = (lat: number) => toRadians(lat) * 6_371_000;
  const ax = east(start.lng);
  const ay = north(start.lat);
  const bx = east(end.lng);
  const by = north(end.lat);
  const px = east(point.lng);
  const py = north(point.lat);
  const dx = bx - ax;
  const dy = by - ay;
  const span = dx * dx + dy * dy;
  const t = span === 0 ? 0 : Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / span));
  const snapped = {
    lat: start.lat + (end.lat - start.lat) * t,
    lng: start.lng + (end.lng - start.lng) * t,
  };
  return { point: snapped, distanceM: haversineMeters(snapped, point), alongSegmentM: t * lengthM };
}

// The rider stays at the pickup. Drop off moves only the driver.
export function riderPosition(_status: string | undefined, pickup: LatLng, _driver: LatLng | null): LatLng {
  return pickup;
}

export function finishedPlace(
  ride: { status: string; dropoff: LatLng & { address?: string } } | null | undefined,
) {
  if (ride?.status !== "Completed" || !isPlotted(ride.dropoff)) return null;
  const address = ride.dropoff.address?.trim() || "Current location";
  return { name: address, address, lat: ride.dropoff.lat, lng: ride.dropoff.lng };
}

export function mapAtFinishedPlace(dropoff: LatLng & { address: string }) {
  const here = { lat: dropoff.lat, lng: dropoff.lng, address: dropoff.address };
  return { pickup: here, route: [] as LatLng[], markers: [{ ...here, kind: "pickup" as const }] };
}

export function linePoints(route: GeoLine | null | undefined): LatLng[] {
  if (!route) return [];
  return route.coordinates
    .filter((pair) => pair.length >= 2 && Number.isFinite(pair[0]) && Number.isFinite(pair[1]))
    .map(([lng, lat]) => ({ lng, lat }));
}

function toRadians(value: number): number {
  return (value * Math.PI) / 180;
}

export function haversineMeters(from: LatLng, to: LatLng): number {
  const earth = 6371000;
  const latDelta = toRadians(to.lat - from.lat);
  const lngDelta = toRadians(to.lng - from.lng);
  const a =
    Math.sin(latDelta / 2) ** 2 +
    Math.cos(toRadians(from.lat)) * Math.cos(toRadians(to.lat)) * Math.sin(lngDelta / 2) ** 2;
  return 2 * earth * Math.asin(Math.min(1, Math.sqrt(a)));
}

export function bearing(from: LatLng, to: LatLng): number {
  const y = Math.sin(toRadians(to.lng - from.lng)) * Math.cos(toRadians(to.lat));
  const x =
    Math.cos(toRadians(from.lat)) * Math.sin(toRadians(to.lat)) -
    Math.sin(toRadians(from.lat)) * Math.cos(toRadians(to.lat)) * Math.cos(toRadians(to.lng - from.lng));
  return (Math.atan2(y, x) * 180) / Math.PI;
}
