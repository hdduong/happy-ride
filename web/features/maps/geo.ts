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
  return remainingLine(line, progress);
}

// Drop the road already driven. The bike stays on the line, and the line continues to the destination.
export function remainingLine(points: LatLng[], current: LatLng, maxDistanceM = 60): LatLng[] {
  if (points.length < 2 || !isPlotted(current)) return points;
  let best = Number.POSITIVE_INFINITY;
  let index = 0;
  let snap = points[0];
  for (let i = 0; i < points.length - 1; i++) {
    const projected = closestOnSegment(points[i], points[i + 1], current);
    if (projected.distanceM < best) {
      best = projected.distanceM;
      index = i;
      snap = projected.point;
    }
  }
  if (best > maxDistanceM) return points;
  const ahead = points.slice(index + 1);
  if (ahead.length === 0) return [];
  if (haversineMeters(snap, ahead[0]) < 1) return ahead.length >= 2 ? ahead : [];
  return [snap, ...ahead];
}

function closestOnSegment(start: LatLng, end: LatLng, point: LatLng): { point: LatLng; distanceM: number } {
  const lengthM = haversineMeters(start, end);
  if (lengthM < 0.5) return { point: start, distanceM: haversineMeters(start, point) };
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
  return { point: snapped, distanceM: haversineMeters(snapped, point) };
}

export function riderPosition(status: string | undefined, pickup: LatLng, driver: LatLng | null): LatLng {
  if ((status === "InProgress" || status === "DroppedOff") && isPlotted(driver)) return driver;
  return pickup;
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
