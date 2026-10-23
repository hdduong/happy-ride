import { bearing, isPlotted, mapAtFinishedPlace, riderPosition, routeForStatus } from "@/features/maps/geo";
import type { MapMarker } from "@/features/maps/MapCanvas";
import type { RiderBooking } from "./useRiderBooking";

export function riderMap({ ride, live, pickup: selected, destination, quote }: Pick<RiderBooking, "ride" | "live" | "pickup" | "destination" | "quote">) {
  if (ride?.status === "Completed" && isPlotted(ride.dropoff)) {
    return { ...mapAtFinishedPlace(ride.dropoff), driverLive: null };
  }
  const pickup = ride?.pickup ?? {
    lat: selected.lat,
    lng: selected.lng,
    address: selected.name,
  };
  const dropoff =
    ride?.dropoff ??
    (destination
      ? { lat: destination.lat, lng: destination.lng, address: destination.name || destination.address }
      : null);
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
  const route = routeForStatus(
    ride?.status,
    ride?.approachRoute,
    ride ? ride.route : quote?.route,
    dropoff && (ride || quote) && isPlotted(dropoff) ? [pickup, dropoff] : [],
    driverPoint,
    ride?.rideId,
  );
  const headingTarget =
    ride && (ride.status === "InProgress" || ride.status === "DroppedOff" || ride.status === "Completed")
      ? ride.dropoff
      : pickup;
  const heading =
    driverLive?.heading != null ? driverLive.heading : driverPoint ? bearing(driverPoint, headingTarget) : 0;
  const showBike = Boolean(
    driverPoint &&
      ride &&
      ride.status !== "Searching" &&
      ride.status !== "NoDriver" &&
      ride.status !== "Cancelled",
  );
  const together = ride?.status === "InProgress" || ride?.status === "DroppedOff";
  const riderPoint = riderPosition(ride?.status, pickup, driverPoint);
  const markers: MapMarker[] = [];
  if (isPlotted(riderPoint)) markers.push({ ...riderPoint, kind: "pickup", offset: together ? [-16, 8] : undefined });
  if (dropoff && isPlotted(dropoff) && (ride || quote)) markers.push({ ...dropoff, kind: "dropoff" });
  if (showBike && driverPoint && isPlotted(driverPoint)) {
    markers.push({ ...driverPoint, kind: "bike", heading });
  }

  return { pickup, route, markers, driverLive };
}
