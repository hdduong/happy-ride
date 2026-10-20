import { bearing, isPlotted, linePoints } from "@/features/maps/geo";
import type { MapMarker } from "@/features/maps/MapCanvas";
import type { RiderBooking } from "./useRiderBooking";

export function riderMap({ ride, live, pickup: selected, destination, quote }: Pick<RiderBooking, "ride" | "live" | "pickup" | "destination" | "quote">) {
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
  const quotedLine = ride ? linePoints(ride.route) : quote ? linePoints(quote.route) : [];
  const route =
    quotedLine.length >= 2 ? quotedLine : dropoff && (ride || quote) && isPlotted(dropoff) ? [pickup, dropoff] : [];
  const driverLive = live && ride && live.rideId === ride.rideId ? live : null;
  const driverPoint = driverLive
    ? { lat: driverLive.lat, lng: driverLive.lng }
    : ride?.driver
      ? { lat: ride.driver.lat, lng: ride.driver.lng }
      : null;
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
  const markers: MapMarker[] = [];
  if (isPlotted(pickup)) markers.push({ ...pickup, kind: "pickup" });
  if (dropoff && isPlotted(dropoff) && (ride || quote)) markers.push({ ...dropoff, kind: "dropoff" });
  if (showBike && driverPoint && isPlotted(driverPoint)) {
    markers.push({ ...driverPoint, kind: "bike", heading });
  }

  return { pickup, route, markers, driverLive };
}
