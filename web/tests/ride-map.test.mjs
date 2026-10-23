import assert from "node:assert/strict";
import test from "node:test";
import { finishedPlace, mapAtFinishedPlace, riderPosition, routeForStatus } from "../features/maps/geo.ts";

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

test("a started ride keeps the rider on the bike", () => {
  const pickup = { lat: 21.03, lng: 105.82 };
  const driver = { lat: 21.036, lng: 105.831 };
  assert.deepEqual(riderPosition("InProgress", pickup, driver), driver);
  assert.deepEqual(riderPosition("DroppedOff", pickup, driver), driver);
  assert.deepEqual(riderPosition("Accepted", pickup, driver), pickup);
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
