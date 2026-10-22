import assert from "node:assert/strict";
import test from "node:test";
import { basemapStyle } from "../features/maps/basemapStyle.ts";

test("a VietMap style is drawn from the matching light raster tiles", () => {
  const style = basemapStyle("https://maps.vietmap.vn/maps/styles/tm/style.json?apikey=test-key");
  assert.equal(typeof style, "object");
  if (typeof style === "string") return;
  const source = style.sources.vietmap;
  assert.equal(source.type, "raster");
  if (source.type !== "raster") return;
  assert.deepEqual(source.tiles, ["https://maps.vietmap.vn/api/tm/{z}/{x}/{y}.png?apikey=test-key"]);
  assert.equal(source.maxzoom, 20);
});

test("other basemap styles stay unchanged", () => {
  const styleUrl = "https://tiles.openfreemap.org/styles/dark";
  assert.equal(basemapStyle(styleUrl), styleUrl);
});
