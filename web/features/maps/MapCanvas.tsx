"use client";

import { useEffect, useState } from "react";
import type { GeoJSONSource, Map as MapLibreMap, Marker } from "maplibre-gl";
import { getMapConfig } from "./api";
import { GoogleDarkMap } from "./GoogleDarkMap";
import type { LatLng, MapClientConfig, MapMarker } from "./types";

export type { MapMarker };

const darkStyle = "https://tiles.openfreemap.org/styles/dark";

export function MapCanvas({
  userId = null,
  center,
  route = [],
  markers = [],
  pulse = null,
  zoom = 15,
}: {
  userId?: string | null;
  center: LatLng;
  route?: LatLng[];
  markers?: MapMarker[];
  pulse?: LatLng | null;
  zoom?: number;
}) {
  const [config, setConfig] = useState<MapClientConfig | null>(null);

  useEffect(() => {
    let cancel = false;
    const load = () => {
      void getMapConfig(userId)
        .then((payload) => {
          if (!cancel) setConfig(payload);
        })
        .catch(() => {
          if (!cancel) setConfig({ provider: "Fake", styleUrl: darkStyle, browserKey: null, region: "VN", language: "vi" });
        });
    };
    load();
    window.addEventListener("happy-ride-map-config", load);
    return () => {
      cancel = true;
      window.removeEventListener("happy-ride-map-config", load);
    };
  }, [userId]);

  if (config?.provider === "Google" && config.browserKey) {
    return (
      <div className="map-canvas">
        <GoogleDarkMap
          apiKey={config.browserKey}
          region={config.region || "VN"}
          language={config.language || "vi"}
          center={center}
          zoom={zoom}
          route={route}
          markers={markers}
          pulse={pulse}
        />
      </div>
    );
  }

  return (
    <LibreDarkMap
      styleUrl={config?.styleUrl || darkStyle}
      center={center}
      zoom={zoom}
      route={route}
      markers={markers}
      pulse={pulse}
    />
  );
}

function LibreDarkMap({
  styleUrl,
  center,
  zoom,
  route,
  markers,
  pulse,
}: {
  styleUrl: string;
  center: LatLng;
  zoom: number;
  route: LatLng[];
  markers: MapMarker[];
  pulse: LatLng | null;
}) {
  const [host, setHost] = useState<HTMLDivElement | null>(null);
  const [map, setMap] = useState<MapLibreMap | null>(null);

  useEffect(() => {
    if (!host) return;
    let created: MapLibreMap | null = null;
    let cancelled = false;
    void import("maplibre-gl").then((maplibregl) => {
      if (cancelled) return;
      const next = new maplibregl.Map({
        container: host,
        style: styleUrl,
        center: [center.lng, center.lat],
        zoom,
      });
      created = next;
      next.addControl(new maplibregl.NavigationControl({ showCompass: false }), "top-right");
      next.on("load", () => {
        next.addSource("route", { type: "geojson", data: lineData(route) });
        next.addLayer({
          id: "route-casing",
          type: "line",
          source: "route",
          paint: { "line-color": "#3a3d44", "line-width": 8, "line-opacity": 0.9 },
        });
        next.addLayer({
          id: "route",
          type: "line",
          source: "route",
          paint: { "line-color": "#ffffff", "line-width": 4 },
        });
        setMap(next);
      });
    });
    return () => {
      cancelled = true;
      created?.remove();
      setMap(null);
    };
  }, [host, styleUrl, zoom]);

  useEffect(() => {
    if (!map?.getSource("route")) return;
    const source = map.getSource("route") as GeoJSONSource | undefined;
    source?.setData(lineData(route));
    const points = [...route, ...markers];
    if (pulse) points.push(pulse);
    if (points.length === 0) points.push(center);
    void import("maplibre-gl").then((module) => {
      if (points.length < 2) {
        const only = points[0] ?? center;
        map.jumpTo({ center: [only.lng, only.lat], zoom });
        return;
      }
      const bounds = new module.LngLatBounds();
      for (const point of points) bounds.extend([point.lng, point.lat]);
      map.fitBounds(bounds, { padding: { top: 80, bottom: 280, left: 48, right: 48 }, maxZoom: 16, duration: 0 });
    });
  }, [map, route, center, markers, pulse, zoom]);

  useEffect(() => {
    if (!map) return;
    let current: Marker[] = [];
    let cancelled = false;
    void import("maplibre-gl").then((module) => {
      if (cancelled) return;
      const maplibregl = module;
      current = markers.map((marker) => {
        const element = document.createElement("div");
        const glyph = document.createElement("div");
        glyph.className = marker.kind === "bike" ? "map-bike" : marker.kind === "dropoff" ? "map-dropoff" : "map-pickup";
        if (marker.kind === "bike" && marker.heading != null) glyph.style.transform = `rotate(${marker.heading}deg)`;
        element.appendChild(glyph);
        return new maplibregl.Marker({ element }).setLngLat([marker.lng, marker.lat]).addTo(map);
      });
      if (pulse) {
        const element = document.createElement("div");
        element.className = "map-pulse-anchor";
        const ring = document.createElement("div");
        ring.className = "map-pulse";
        element.appendChild(ring);
        current.push(new maplibregl.Marker({ element }).setLngLat([pulse.lng, pulse.lat]).addTo(map));
      }
    });
    return () => {
      cancelled = true;
      for (const marker of current) marker.remove();
    };
  }, [map, markers, pulse]);

  return <div className="map-canvas" ref={setHost} />;
}

function lineData(route: LatLng[]) {
  return {
    type: "Feature" as const,
    properties: {},
    geometry: {
      type: "LineString" as const,
      coordinates: route.map((point) => [point.lng, point.lat]),
    },
  };
}
