import assert from "node:assert/strict";
import test from "node:test";
import { finishedPlace, mapAtFinishedPlace, remainingLine, riderPosition, routeForStatus } from "../features/maps/geo.ts";
import { driverMap } from "../features/driver/driverMap.ts";
import { riderMap } from "../features/rider/riderMap.ts";

test("an accepted ride draws the road to the rider", () => {
  const route = routeForStatus(
    "Accepted",
    { type: "LineString", coordinates: [[105.8, 21.02], [105.81, 21.025], [105.82, 21.03]] },
    { type: "LineString", coordinates: [[105.82, 21.03], [105.85, 21.04]] },
    [],
  );
  assert.deepEqual(route.map((point) => [point.lng, point.lat]), [
    [105.8, 21.02],
    [105.81, 21.025],
    [105.82, 21.03],
  ]);
});

test("the line starts at the bike after the spur behind it has been passed", () => {
  const approach = {
    type: "LineString",
    coordinates: [
      [105.82, 21.03],
      [105.82, 21.0304],
      [105.823, 21.0304],
      [105.826, 21.028],
    ],
  };
  const trip = { type: "LineString", coordinates: [[105.826, 21.028], [105.83, 21.02]] };
  const bike = { lng: 105.8215, lat: 21.0304 };
  const route = routeForStatus("Accepted", approach, trip, [], bike);
  assert.ok(route[0].lng > 105.8204, "the line still starts on the spur behind the bike");
  assert.equal(route.at(-1).lng, 105.826);
  assert.ok(route.every((point) => point.lat > 21.0302 || point.lng > 105.823));

  const tripLine = routeForStatus("InProgress", approach, {
    type: "LineString",
    coordinates: approach.coordinates,
  }, [], bike);
  assert.ok(tripLine[0].lng > 105.8204);
  const away = remainingLine(routeForStatus("Accepted", approach, trip, []), { lat: 21.05, lng: 105.9 });
  assert.ok(away[0].lng > 105.8204, "a sample off the road restores the spur behind the bike");
  assert.equal(away.at(-1).lng, 105.826);
});

test("a nearby later leg does not erase the road still ahead", () => {
  const outAndBack = [
    { lng: 105.82, lat: 21.03 },
    { lng: 105.825, lat: 21.03 },
    { lng: 105.825, lat: 21.02992 },
    { lng: 105.82, lat: 21.02992 },
  ];
  const onTheWayOut = remainingLine(outAndBack, { lng: 105.822, lat: 21.03 });
  assert.ok(onTheWayOut.some((point) => point.lng === 105.825 && point.lat === 21.03));
  assert.ok(onTheWayOut[0].lng > 105.8205);
  const stillOut = remainingLine(outAndBack, { lng: 105.822, lat: 21.02992 });
  assert.ok(stillOut.some((point) => point.lng === 105.825 && point.lat === 21.03));
});

test("reaching the destination removes the last driven segment", () => {
  const line = [
    { lng: 105.821, lat: 21.031 },
    { lng: 105.827, lat: 21.029 },
  ];
  const underway = remainingLine(line, { lng: 105.824, lat: 21.03 });
  assert.ok(underway.length >= 2);
  assert.ok(underway[0].lng > 105.823);
  assert.equal(underway.at(-1).lng, 105.827);
  assert.deepEqual(remainingLine(line, { lng: 105.827, lat: 21.029 }), []);
  assert.deepEqual(routeForStatus("DroppedOff", null, {
    type: "LineString",
    coordinates: [[105.82, 21.03], [105.826, 21.028]],
  }, line, { lng: 105.826, lat: 21.028 }), []);
});

test("arrival clears a short final segment", () => {
  const line = [
    { lat: 21.03, lng: 105.82 },
    { lat: 21.03, lng: 105.825 },
    { lat: 21.03005, lng: 105.825 },
  ];
  const rideId = "short-final-segment-regression";
  remainingLine(line, line[1], 60, rideId);
  assert.deepEqual(remainingLine(line, line[2], 60, rideId), []);
});

test("a second booking of the same road starts the line at the new bike", () => {
  const trip = { type: "LineString", coordinates: [[105.84, 21.04], [105.846, 21.038]] };
  const end = { lng: 105.846, lat: 21.038 };
  assert.deepEqual(routeForStatus("InProgress", null, trip, [], end, "ride-a"), []);
  const restart = routeForStatus("InProgress", null, trip, [], { lng: 105.841, lat: 21.039667 }, "ride-b");
  assert.ok(restart.length >= 2);
  assert.ok(restart[0].lng > 105.8404);
  assert.ok(restart[0].lng < 105.842);
  assert.equal(restart.at(-1).lng, 105.846);
});

test("a location off the road keeps the last trimmed line", () => {
  const line = [
    { lng: 105.9, lat: 21.03 },
    { lng: 105.91, lat: 21.03 },
  ];
  const trimmed = remainingLine(line, { lng: 105.905, lat: 21.03 });
  assert.ok(trimmed[0].lng > 105.904);
  assert.ok(trimmed[0].lng < 105.906);
  const offRoad = remainingLine(line, { lng: 105.905, lat: 21.032 });
  assert.ok(Math.abs(offRoad[0].lng - trimmed[0].lng) < 0.00002);
  assert.equal(offRoad.at(-1).lng, 105.91);
  const jumpedBack = remainingLine(line, { lng: trimmed[0].lng - 0.0004, lat: 21.03 });
  assert.ok(Math.abs(jumpedBack[0].lng - trimmed[0].lng) < 0.00002);
});

test("backward jitter does not redraw the road already passed", () => {
  const line = [
    { lng: 105.93, lat: 21.04 },
    { lng: 105.94, lat: 21.04 },
  ];
  const forward = remainingLine(line, { lng: 105.935, lat: 21.04 });
  const jitter = remainingLine(line, { lng: forward[0].lng - 0.00009, lat: 21.04 });
  assert.ok(jitter[0].lng >= forward[0].lng - 0.00001);
  assert.equal(jitter.at(-1).lng, 105.94);
  const further = remainingLine(line, { lng: 105.937, lat: 21.04 });
  assert.ok(further[0].lng > jitter[0].lng);
});

test("a far sample after arrival does not restore the finished route", () => {
  const line = [
    { lng: 105.95, lat: 21.02 },
    { lng: 105.956, lat: 21.02 },
  ];
  assert.deepEqual(remainingLine(line, { lng: 105.956, lat: 21.02 }), []);
  assert.deepEqual(remainingLine(line, { lng: 105.956, lat: 21.022 }), []);
});

test("an unmatched bike still shows the whole road", () => {
  const line = [
    { lng: 105.96, lat: 21.01 },
    { lng: 105.97, lat: 21.01 },
  ];
  const shown = remainingLine(line, { lng: 105.965, lat: 21.012 });
  assert.equal(shown[0].lng, 105.96);
  assert.equal(shown.at(-1).lng, 105.97);
});

test("a started ride keeps the rider on the bike until drop-off", () => {
  const pickup = { lat: 21.03, lng: 105.82 };
  const dropoff = { lat: 21.035, lng: 105.821 };
  const driver = { lat: 21.036, lng: 105.831 };
  assert.deepEqual(riderPosition("InProgress", pickup, driver, dropoff), driver);
  assert.deepEqual(riderPosition("DroppedOff", pickup, driver, dropoff), dropoff);
  assert.deepEqual(riderPosition("Completed", pickup, driver, dropoff), dropoff);
  assert.deepEqual(riderPosition("Accepted", pickup, driver, dropoff), pickup);
});

test("after drop-off the rider stays at the destination while only the driver moves", () => {
  const pickup = { lat: 21.03342, lng: 105.82845, address: "Lẩu Ếch Hà My" };
  const dropoff = { lat: 21.0315, lng: 105.84, address: "Công viên Lê-nin" };
  const roaming = {
    rideId: "ride-roam",
    driverId: "driver-1",
    lat: 21.0342,
    lng: 105.8418,
    heading: 15,
    speedKmh: 36,
    at: "2026-10-23T12:00:05Z",
  };
  const ride = {
    rideId: "ride-roam",
    status: "DroppedOff",
    pickup,
    dropoff,
    distanceM: 1400,
    durationS: 240,
    fareVnd: 16000,
    route: { type: "LineString", coordinates: [[pickup.lng, pickup.lat], [dropoff.lng, dropoff.lat]] },
    round: 1,
    driver: { userId: "driver-1", name: "HanoiD02", plate: "29-P1 200.02", lat: dropoff.lat, lng: dropoff.lng },
  };
  for (const status of ["DroppedOff", "Completed"]) {
    const view = riderMap({
      ride: { ...ride, status },
      live: roaming,
      pickup: { id: "home", name: pickup.address, address: pickup.address, lat: pickup.lat, lng: pickup.lng },
      destination: null,
      quote: null,
    });
    assert.deepEqual(view.route, []);
    assert.deepEqual(view.markers, [
      { ...dropoff, kind: "pickup" },
      { lat: roaming.lat, lng: roaming.lng, kind: "bike", heading: 15 },
    ]);
  }
  const driverView = driverMap({
    ride,
    live: roaming,
    offer: null,
    center: pickup,
    me: { id: "driver-1", name: "HanoiD02", role: "Driver", city: "Hanoi", plate: "29-P1 200.02", lat: dropoff.lat, lng: dropoff.lng },
  });
  assert.deepEqual(driverView.route, []);
  assert.deepEqual(driverView.markers, [
    { ...dropoff, kind: "pickup", offset: undefined },
    { lat: roaming.lat, lng: roaming.lng, kind: "bike", heading: 15 },
  ]);
});

test("a finished ride leaves the rider at the drop-off", () => {
  const dropoff = { lat: 21.035, lng: 105.821, address: "Phố Đội Cấn" };
  const ride = {
    status: "Completed",
    pickup: { lat: 21.033, lng: 105.828, address: "Lẩu Ếch Hà My" },
    dropoff,
    route: { type: "LineString", coordinates: [[105.828, 21.033], [105.821, 21.035]] },
  };
  assert.deepEqual(finishedPlace(ride), {
    name: "Phố Đội Cấn",
    address: "Phố Đội Cấn",
    lat: 21.035,
    lng: 105.821,
  });
  assert.deepEqual(mapAtFinishedPlace(dropoff), {
    pickup: dropoff,
    route: [],
    markers: [{ ...dropoff, kind: "pickup" }],
  });
  assert.equal(finishedPlace({ ...ride, status: "Cancelled" }), null);
});
