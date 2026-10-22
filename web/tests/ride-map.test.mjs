import assert from "node:assert/strict";
import test from "node:test";
import { riderPosition, routeForStatus } from "../features/maps/geo.ts";

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
