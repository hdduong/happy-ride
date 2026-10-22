import assert from "node:assert/strict";
import test from "node:test";
import { bikeMarkerSvgFor } from "../features/maps/markerIcons.ts";

test("the bike glyph rotates inside the badge so the marker can still position itself", () => {
  assert.match(bikeMarkerSvgFor(90), /<g transform="rotate\(0 16 16\)"/);
  assert.match(bikeMarkerSvgFor(0), /<g transform="rotate\(-90 16 16\)"/);
  assert.match(bikeMarkerSvgFor(0), /<circle cx="16" cy="16"/);
});
