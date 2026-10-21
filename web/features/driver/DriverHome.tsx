"use client";

import { MapCanvas } from "@/features/maps/MapCanvas";
import { Stage } from "@/shared/ui";
import { DriverSheet } from "./components/DriverSheet";
import { RideBanner } from "./components/RideBanner";
import { driverMap } from "./driverMap";
import { useDriverDashboard } from "./useDriverDashboard";

export function DriverHome({ userId }: { userId: string }) {
  const dashboard = useDriverDashboard(userId);
  const map = driverMap(dashboard);
  return (
    <Stage
      map={<MapCanvas userId={userId} center={map.center} route={map.route} markers={map.markers} />}
      banner={dashboard.ride ? <RideBanner ride={dashboard.ride} /> : null}
      sheet={<DriverSheet dashboard={dashboard} />}
    />
  );
}
