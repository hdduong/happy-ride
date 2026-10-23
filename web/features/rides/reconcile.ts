import type { RideView } from "./types";

const STATUS_ORDER = [
  "Searching",
  "Accepted",
  "DriverArrived",
  "InProgress",
  "DroppedOff",
  "Completed",
  "NoDriver",
  "Cancelled",
];

export function isActiveStatus(status: string): boolean {
  return (
    status === "Searching" ||
    status === "Accepted" ||
    status === "DriverArrived" ||
    status === "InProgress" ||
    status === "DroppedOff"
  );
}

function statusRank(status: string): number {
  const index = STATUS_ORDER.indexOf(status);
  return index === -1 ? STATUS_ORDER.length : index;
}

export function preferForward(previous: RideView | null, incoming: RideView): RideView {
  if (!previous || previous.rideId !== incoming.rideId) return incoming;
  if (!isActiveStatus(previous.status) && previous.status !== incoming.status) return previous;
  if (statusRank(incoming.status) < statusRank(previous.status)) return previous;
  return incoming;
}

export function applyRiderUpdate(
  previous: RideView | null,
  incoming: RideView | null,
  ignored: ReadonlySet<string>,
): RideView | null {
  // The confirmation screen already holds this ride. A new visit, or a delayed
  // event for a different trip, must not draw that finished route again.
  if (incoming?.status === "Completed" && previous?.rideId !== incoming.rideId) {
    return previous;
  }
  if (incoming && ignored.has(incoming.rideId) && !isActiveStatus(incoming.status)) {
    return previous && !isActiveStatus(previous.status) ? previous : null;
  }
  if (!incoming) {
    if (previous && !isActiveStatus(previous.status)) return previous;
    return null;
  }
  return preferForward(previous, incoming);
}
