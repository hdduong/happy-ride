"use client";

import { useEffect, useRef, useState } from "react";
import { getMapConfig } from "./api";
import { basemapStyle } from "./basemapStyle";
import { GoogleDarkMap } from "./GoogleDarkMap";
import { bikeMarkerSvg, personMarkerSvg } from "./markerIcons";
import type { LatLng, MapClientConfig, MapMarker } from "./types";

export type { MapMarker };

const darkStyle = "https://tiles.openfreemap.org/styles/dark";

type LngLatBounds = { extend(lngLat: [number, number]): void };
type GeoJsonSource = { setData(data: unknown): void };
type VietMap = {
  addControl(control: unknown, position?: string): void;
  on(event: "load" | "error" | "zoomend" | "dragend", handler: (event?: { originalEvent?: Event }) => void): void;
  addSource(id: string, source: unknown): void;
  addLayer(layer: unknown): void;
  getSource(id: string): GeoJsonSource | undefined;
  getZoom(): number;
  jumpTo(options: { center: [number, number]; zoom: number }): void;
  fitBounds(bounds: LngLatBounds, options: unknown): void;
  remove(): void;
};
type VietMapMarker = {
  setLngLat(lngLat: [number, number]): VietMapMarker;
  addTo(map: VietMap): VietMapMarker;
  remove(): void;
};
type VietMapGl = {
  Map: new (options: { container: HTMLElement; style: string | object; center: [number, number]; zoom: number; maxZoom: number }) => VietMap;
  NavigationControl: new (options: { showCompass: boolean }) => unknown;
  Marker: new (options: { element: HTMLElement }) => VietMapMarker;
  LngLatBounds: new () => LngLatBounds;
};

declare global {
  interface Window {
    vietmapgl?: VietMapGl;
  }
}

let vietMapLoading: Promise<VietMapGl> | null = null;

function loadVietMap() {
  if (window.vietmapgl) return Promise.resolve(window.vietmapgl);
  if (!vietMapLoading) {
    // MapLibre GL 6 downloads VietMap tiles and then rejects them
    // ("Unimplemented type"). VietMap GL 6.0.1 is the fork that draws those
    // tiles. The prebuilt script keeps its own worker intact.
    vietMapLoading = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "/vietmap/vietmap-gl.js";
      script.async = true;
      script.onload = () => {
        if (window.vietmapgl) resolve(window.vietmapgl);
        else reject(new Error("VietMap GL did not start."));
      };
      script.onerror = () => reject(new Error("VietMap GL failed to load."));
      document.head.appendChild(script);
    });
  }
  return vietMapLoading;
}

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

  if (!config) return <div className="map-canvas" />;

  if (config.provider === "Google" && config.browserKey) {
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
  const [map, setMap] = useState<VietMap | null>(null);
  const userMoved = useRef(false);
  const seenCamera = useRef("");

  useEffect(() => {
    if (!host) return;
    let created: VietMap | null = null;
    let cancelled = false;
    const keepUserZoom = (event: Event) => {
      const target = event.target;
      if (target instanceof Element && target.closest(".vietmapgl-ctrl-zoom-in, .vietmapgl-ctrl-zoom-out")) {
        userMoved.current = true;
      }
    };
    host.addEventListener("click", keepUserZoom);
    userMoved.current = false;
    seenCamera.current = "";
    void loadVietMap().then((vietmapgl) => {
      if (cancelled) return;
      const next = new vietmapgl.Map({
        container: host,
        style: basemapStyle(styleUrl),
        center: [center.lng, center.lat],
        zoom,
        maxZoom: 20,
      });
      const rememberZoom = () => {
        host.dataset.zoom = String(next.getZoom());
      };
      next.on("zoomend", (event) => {
        rememberZoom();
        if (event?.originalEvent) userMoved.current = true;
      });
      next.on("dragend", () => {
        userMoved.current = true;
      });
      created = next;
      // This fork throws error events that nobody listened for, which takes down the page.
      next.on("error", () => {});
      next.addControl(new vietmapgl.NavigationControl({ showCompass: false }), "top-right");
      next.on("load", () => {
        if (cancelled) return;
        next.addSource("route", { type: "geojson", data: lineData(route) });
        next.addLayer({
          id: "route-casing",
          type: "line",
          source: "route",
          paint: { "line-color": "#ffffff", "line-width": 8, "line-opacity": 0.95 },
        });
        next.addLayer({
          id: "route",
          type: "line",
          source: "route",
          paint: { "line-color": "#111111", "line-width": 4 },
        });
        rememberZoom();
        setMap(next);
      });
    });
    return () => {
      cancelled = true;
      host.removeEventListener("click", keepUserZoom);
      created?.remove();
      setMap(null);
    };
  }, [host, styleUrl, zoom]);

  useEffect(() => {
    if (!map) return;
    const current = map;
    const source = routeSource(current);
    if (!source) return;
    source.setData(lineData(route));
    const points = [...route, ...markers];
    if (pulse) points.push(pulse);
    if (points.length === 0) points.push(center);
    const key = points.map((point) => `${point.lat.toFixed(5)},${point.lng.toFixed(5)}`).join(";");
    if (userMoved.current || key === seenCamera.current) return;
    seenCamera.current = key;
    void loadVietMap().then((vietmapgl) => {
      try {
        if (points.length < 2) {
          const only = points[0] ?? center;
          current.jumpTo({ center: [only.lng, only.lat], zoom });
          return;
        }
        const bounds = new vietmapgl.LngLatBounds();
        for (const point of points) bounds.extend([point.lng, point.lat]);
        current.fitBounds(bounds, { padding: { top: 80, bottom: 280, left: 48, right: 48 }, maxZoom: 17, duration: 0 });
      } catch {
        // The map was removed while its style was changing.
      }
    });
  }, [map, route, center, markers, pulse, zoom]);

  useEffect(() => {
    if (!map) return;
    let current: VietMapMarker[] = [];
    let cancelled = false;
    void loadVietMap().then((vietmapgl) => {
      if (cancelled) return;
      try {
        current = markers.map((marker) => {
          const element = document.createElement("div");
          element.className = marker.kind === "bike" ? "map-bike" : marker.kind === "dropoff" ? "map-dropoff" : "map-rider";
          if (marker.kind === "bike") {
            element.innerHTML = bikeMarkerSvg;
            element.style.transform = `rotate(${marker.heading ?? 0}deg)`;
          } else if (marker.kind !== "dropoff") {
            element.innerHTML = personMarkerSvg;
          }
          return new vietmapgl.Marker({ element }).setLngLat([marker.lng, marker.lat]).addTo(map);
        });
        if (pulse) {
          const element = document.createElement("div");
          element.className = "map-pulse-anchor";
          const ring = document.createElement("div");
          ring.className = "map-pulse";
          element.appendChild(ring);
          current.push(new vietmapgl.Marker({ element }).setLngLat([pulse.lng, pulse.lat]).addTo(map));
        }
      } catch {
        // The map was removed while its style was changing.
      }
    });
    return () => {
      cancelled = true;
      for (const marker of current) marker.remove();
    };
  }, [map, markers, pulse]);

  return <div className="map-canvas" ref={setHost} />;
}

function routeSource(map: VietMap | null) {
  if (!map) return undefined;
  try {
    return map.getSource("route");
  } catch {
    return undefined;
  }
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
