import assert from "node:assert/strict";
import test from "node:test";
import { bikeMarkerSvgFor } from "../features/maps/markerIcons.ts";

test("the bike glyph rotates inside the badge so the marker can still position itself", () => {
  const svg = bikeMarkerSvgFor(90);
  assert.match(svg, /<g transform="rotate\(90 16 16\)"/);
  assert.match(svg, /<circle cx="16" cy="16"/);
});
