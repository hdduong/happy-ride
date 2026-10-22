type MapConfigConnection = {
  on(event: "MapConfigChanged", callback: () => void): void;
  onreconnected(callback: () => void): void;
};

export function notifyMapConfigChanged() {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new Event("happy-ride-map-config"));
}

// The API restart that applies a new map provider drops the hub event.
// Reconnecting is the signal that the new process can serve map config.
export function bindMapConfigRefresh(connection: MapConfigConnection, isActive: () => boolean) {
  const refresh = () => {
    if (!isActive()) return;
    notifyMapConfigChanged();
  };
  connection.on("MapConfigChanged", refresh);
  connection.onreconnected(refresh);
}
