"use client";

import { MapCanvas } from "@/features/maps/MapCanvas";
import { Stage } from "@/shared/ui";
import { BookingSheet } from "./components/BookingSheet";
import { riderMap } from "./riderMap";
import { useRiderBooking } from "./useRiderBooking";

export function RiderHome({ userId }: { userId: string }) {
  const booking = useRiderBooking(userId);
  const map = riderMap(booking);
  return (
    <Stage
      map={<MapCanvas center={map.pickup} route={map.route} markers={map.markers}
        pulse={booking.ride?.status === "Searching" ? map.pickup : null} />}
      sheet={<BookingSheet booking={booking} driverLive={map.driverLive} />}
    />
  );
}
