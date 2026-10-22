export function basemapStyle(styleUrl: string): string | {
  version: 8;
  sources: { vietmap: { type: "raster"; tiles: string[]; tileSize: number; attribution: string } };
  layers: { id: string; type: "raster"; source: "vietmap" }[];
} {
  let parsed: URL;
  try {
    parsed = new URL(styleUrl);
  } catch {
    return styleUrl;
  }
  const parts = parsed.pathname.split("/");
  const name = parts[3];
  const key = parsed.searchParams.get("apikey");
  if (parsed.hostname !== "maps.vietmap.vn" || parts[1] !== "maps" || parts[2] !== "styles" || parts[4] !== "style.json" || !name || !key) {
    return styleUrl;
  }
  return {
    version: 8,
    sources: {
      vietmap: {
        type: "raster",
        tiles: [`https://maps.vietmap.vn/api/${encodeURIComponent(name)}/{z}/{x}/{y}.png?apikey=${encodeURIComponent(key)}`],
        tileSize: 256,
        attribution: "© VietMap",
      },
    },
    layers: [{ id: "vietmap-base", type: "raster", source: "vietmap" }],
  };
}
