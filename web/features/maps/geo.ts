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
