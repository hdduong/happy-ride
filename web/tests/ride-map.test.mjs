import assert from "node:assert/strict";
import test from "node:test";
import { remainingLine, riderPosition, routeForStatus } from "../features/maps/geo.ts";

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
  assert.deepEqual(remainingLine(routeForStatus("Accepted", approach, trip, []), { lat: 21.05, lng: 105.9 }).at(0), {
    lng: 105.82,
    lat: 21.03,
  });
});

test("reaching the destination removes the last driven segment", () => {
  const line = [
    { lng: 105.82, lat: 21.03 },
    { lng: 105.826, lat: 21.028 },
  ];
  assert.deepEqual(remainingLine(line, { lng: 105.826, lat: 21.028 }), []);
  const underway = remainingLine(line, { lng: 105.823, lat: 21.029 });
  assert.ok(underway.length >= 2);
  assert.ok(underway[0].lng > 105.822);
  assert.equal(underway.at(-1).lng, 105.826);
  assert.deepEqual(routeForStatus("DroppedOff", null, {
    type: "LineString",
    coordinates: [[105.82, 21.03], [105.826, 21.028]],
  }, line, { lng: 105.826, lat: 21.028 }), []);
});

test("a started ride keeps the rider on the bike", () => {
  const pickup = { lat: 21.03, lng: 105.82 };
  const driver = { lat: 21.036, lng: 105.831 };
  assert.deepEqual(riderPosition("InProgress", pickup, driver), driver);
  assert.deepEqual(riderPosition("DroppedOff", pickup, driver), driver);
  assert.deepEqual(riderPosition("Accepted", pickup, driver), pickup);
});
