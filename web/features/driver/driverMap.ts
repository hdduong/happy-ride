import { bearing, isPlotted, riderPosition, routeForStatus } from "../maps/geo";
import type { MapMarker } from "../maps/MapCanvas";
import type { DriverDashboard } from "./useDriverDashboard";

export function driverMap({ ride, live, offer, center, me }: Pick<DriverDashboard, "ride" | "live" | "offer" | "center" | "me">) {
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
  const route = routeForStatus(
    ride?.status,
    ride?.approachRoute,
    ride?.route,
    ride && isPlotted(ride.pickup) && isPlotted(ride.dropoff)
      ? [ride.pickup, ride.dropoff]
      : offer && isPlotted(offer.pickup) && isPlotted(offer.dropoff)
        ? [offer.pickup, offer.dropoff]
        : [],
    driverPoint,
    ride?.rideId,
  );
  const markers: MapMarker[] = [];
  const pickupPoint = ride?.pickup ?? offer?.pickup ?? null;
  const dropoffPoint = ride?.dropoff ?? offer?.dropoff ?? null;
  const dropped = Boolean(ride && (ride.status === "DroppedOff" || ride.status === "Completed"));
  const together = ride?.status === "InProgress";
  const riderPoint = pickupPoint ? riderPosition(ride?.status, pickupPoint, driverPoint, dropoffPoint) : null;
  if (riderPoint && isPlotted(riderPoint) && (ride || offer)) {
    markers.push({ ...riderPoint, kind: "pickup", offset: together ? [-16, 8] : undefined });
  }
  if (!dropped && dropoffPoint && isPlotted(dropoffPoint) && (ride || offer)) {
    markers.push({ ...dropoffPoint, kind: "dropoff" });
  }
  if (driverPoint && ride && isPlotted(driverPoint)) {
    const target = ride.status === "Accepted" || ride.status === "DriverArrived" ? ride.pickup : ride.dropoff;
    const heading = driverLive?.heading != null ? driverLive.heading : bearing(driverPoint, target);
    markers.push({ ...driverPoint, kind: "bike", heading });
  }
  const selfLive = !ride && live && (!me || !live.driverId || live.driverId === me.id) ? live : null;
  const self = selfLive
    ? { lat: selfLive.lat, lng: selfLive.lng, heading: selfLive.heading ?? undefined }
    : me?.lat != null && me.lng != null
      ? { lat: me.lat, lng: me.lng, heading: undefined }
      : null;
  if (!driverPoint && self && isPlotted(self)) markers.push({ ...self, kind: "bike", heading: self.heading });
  const focus = dropped && dropoffPoint && isPlotted(dropoffPoint) ? dropoffPoint : pickupPoint;

  return { center: focus && isPlotted(focus) ? focus : self && isPlotted(self) ? self : center, route, markers };
}
