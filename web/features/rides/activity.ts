import type { ActivityClient } from "../../shared/telemetry/activity";
import { earliestOffer } from "./offers";
import { isActiveStatus } from "./reconcile";
import type { RideSession, SessionRole } from "./session";

// Observe reconciled state, not raw polls/messages. Location ticks and repeated
// snapshots must not create an activity event every two seconds.
export function observeRideActivity(session: RideSession, role: SessionRole, client: ActivityClient): () => void {
  const displayedOffers = new Set<string>();
  function update() {
    const { ride, offers } = session.getSnapshot();
    const active = ride && isActiveStatus(ride.status) && !(role === "driver" && ride.status === "DroppedOff");
    client.setRideContext(active ? ride.rideId : undefined);
    const displayed = role === "driver" && !ride ? earliestOffer(offers) : null;
    if (displayed && !displayedOffers.has(displayed.offerId)) {
      displayedOffers.add(displayed.offerId);
      // Bound memory over long driver sessions. Closed offers also have session
      // tombstones, so trimming this diagnostic set does not revive UI offers.
      if (displayedOffers.size > 256) displayedOffers.delete(displayedOffers.values().next().value!);
      client.track("OfferDisplayed", { rideId: displayed.rideId, offerId: displayed.offerId });
    }
  }
  update();
  return session.subscribe(update);
}
