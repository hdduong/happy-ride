import assert from "node:assert/strict";
import test from "node:test";
import { createRideSession, synchronizeRideSession } from "../features/rides/session.ts";

const ride = (status = "Accepted", rideId = "ride-1") => ({
  rideId, status, pickup: { lat: 10, lng: 106, address: "Pickup" },
  dropoff: { lat: 11, lng: 107, address: "Drop-off" },
  distanceM: 1000, durationS: 300, fareVnd: 15000, round: 1,
  route: { type: "LineString", coordinates: [[106, 10], [107, 11]] }, driver: null,
});
const offer = (offerId = "offer-1") => ({
  offerId, rideId: "ride-1", pickup: ride().pickup, dropoff: ride().dropoff,
  distanceM: 1000, durationS: 300, expiresAt: "", round: 1, pickupDistanceM: 100,
});
function deferred() {
  let resolve;
  const promise = new Promise((done) => { resolve = done; });
  return { promise, resolve };
}
const flush = () => new Promise((resolve) => setImmediate(resolve));

for (const role of ["rider", "driver"]) {
  test(`${role}: a delayed empty poll cannot erase a newer realtime ride`, async () => {
    const session = createRideSession(role);
    const request = deferred();
    let hub;
    const close = synchronizeRideSession(session, {
      load: () => request.promise,
      connect: (handlers) => { hub = handlers; return () => {}; },
      schedule: () => () => {},
    });
    hub.onRide(ride());
    request.resolve({ ride: null, offers: [] });
    await flush();
    assert.equal(session.getSnapshot().ride.status, "Accepted");
    close();
  });

  test(`${role}: commands suppress polling and invalidate snapshots already in flight`, async () => {
    const session = createRideSession(role);
    const oldStamp = session.pollRevision();
    const request = deferred();
    const pending = session.command(async () => {
      await request.promise;
      session.receiveRide(ride("InProgress"));
    });
    assert.equal(session.pollRevision(), null);
    session.receivePoll(oldStamp, { ride: null, offers: [] });
    request.resolve();
    await pending;
    session.receivePoll(oldStamp, { ride: null, offers: [] });
    assert.equal(session.getSnapshot().ride.status, "InProgress");
  });

  test(`${role}: older status snapshots cannot move a ride backwards`, () => {
    const session = createRideSession(role);
    session.receiveRide(ride("InProgress"));
    session.receivePoll(session.pollRevision(), { ride: ride("Accepted"), offers: [] });
    assert.equal(session.getSnapshot().ride.status, "InProgress");
  });
}

test("a dismissed terminal rider ride stays hidden when old events and polls arrive", () => {
  const session = createRideSession("rider");
  session.receiveRide(ride("DroppedOff"));
  session.receiveRide(ride("Completed"));
  session.dismissRide();
  session.receiveRide(ride("Accepted"));
  session.receivePoll(session.pollRevision(), { ride: ride("Completed"), offers: [] });
  assert.equal(session.getSnapshot().ride, null);
  session.receiveRide(ride("Searching", "ride-2"));
  session.receiveRide(ride("Completed"));
  assert.equal(session.getSnapshot().ride.rideId, "ride-2");
});

test("opening the rider again does not restore a completed trip", () => {
  const session = createRideSession("rider");
  session.receivePoll(session.pollRevision(), { ride: ride("Completed"), offers: [] });
  session.receiveRide(ride("Completed"));
  assert.equal(session.getSnapshot().ride, null);

  session.receiveRide(ride("DroppedOff"));
  session.receiveRide(ride("Completed"));
  assert.equal(session.getSnapshot().ride.status, "Completed");
  session.receivePoll(session.pollRevision(), { ride: null, offers: [] });
  assert.equal(session.getSnapshot().ride.status, "Completed");

  const later = createRideSession("rider");
  later.receiveRide(ride("InProgress", "ride-2"));
  later.receivePoll(later.pollRevision(), { ride: ride("Completed"), offers: [] });
  later.receiveRide(ride("Completed"));
  assert.equal(later.getSnapshot().ride.rideId, "ride-2");
  assert.equal(later.getSnapshot().ride.status, "InProgress");
});

test("closed driver rides cannot return and a late closure cannot clear a different ride", () => {
  const session = createRideSession("driver");
  session.receiveRide(ride("Completed"));
  session.receivePoll(session.pollRevision(), { ride: ride(), offers: [] });
  assert.equal(session.getSnapshot().ride, null);
  session.receiveRide(ride("Accepted", "ride-2"));
  session.receiveRide(ride("Cancelled"));
  assert.equal(session.getSnapshot().ride.rideId, "ride-2");
});

test("closed offers cannot be resurrected by either an in-flight or later stale snapshot", () => {
  const session = createRideSession("driver");
  session.receiveOffer(offer());
  const stamp = session.pollRevision();
  session.closeOffer({ offerId: "offer-1", reason: "Taken" });
  session.receivePoll(stamp, { ride: null, offers: [offer()] });
  session.receivePoll(session.pollRevision(), { ride: null, offers: [offer()] });
  session.receiveOffer(offer());
  assert.deepEqual(session.getSnapshot().offers, []);
});

test("a realtime offer survives a poll that started before it was received", () => {
  const session = createRideSession("driver");
  const stamp = session.pollRevision();
  session.receiveOffer(offer());
  session.receivePoll(stamp, { ride: null, offers: [] });
  assert.equal(session.getSnapshot().offers[0].offerId, "offer-1");
});

test("polls run sequentially and cleanup ignores delayed polls and hub callbacks", async () => {
  const session = createRideSession("rider");
  const first = deferred();
  const second = deferred();
  let calls = 0;
  let hub;
  let scheduled;
  let timerCancelled = false;
  let connectionClosed = false;
  const close = synchronizeRideSession(session, {
    load: () => (++calls === 1 ? first.promise : second.promise),
    connect: (handlers) => { hub = handlers; return () => { connectionClosed = true; }; },
    schedule: (callback) => {
      scheduled = callback;
      return () => { timerCancelled = true; };
    },
  });
  assert.equal(calls, 1);
  assert.equal(scheduled, undefined, "another poll must not be scheduled while HTTP is pending");
  first.resolve({ ride: null, offers: [] });
  await flush();
  scheduled();
  assert.equal(calls, 2);
  close();
  second.resolve({ ride: ride(), offers: [] });
  hub.onRide(ride());
  await flush();
  assert.equal(session.getSnapshot().ride, null);
  assert.equal(connectionClosed, true);
  assert.equal(timerCancelled, true);
});

test("location updates do not starve fallback polling", () => {
  const session = createRideSession("rider");
  const stamp = session.pollRevision();
  session.receiveLocation({ rideId: "ride-1", driverId: "driver-1", lat: 10, lng: 106 });
  session.receivePoll(stamp, { ride: ride(), offers: [] });
  assert.equal(session.getSnapshot().ride.status, "Accepted");
});
