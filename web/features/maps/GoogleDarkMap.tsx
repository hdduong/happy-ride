"use client";

import { useEffect, useRef } from "react";
import { APIProvider, Map, useMap, ColorScheme } from "@vis.gl/react-google-maps";
import { bikeMarkerSvgFor, personMarkerSvg } from "./markerIcons";
import type { LatLng, MapMarker } from "./types";

export function GoogleDarkMap({
  apiKey,
  region,
  language,
  center,
  zoom = 15,
  route,
  markers,
  pulse,
}: {
  apiKey: string;
  region: string;
  language: string;
  center: LatLng;
  zoom?: number;
  route: LatLng[];
  markers: MapMarker[];
  pulse: LatLng | null;
}) {
  return (
    <APIProvider apiKey={apiKey} region={region} language={language}>
      <Map
        defaultCenter={{ lat: center.lat, lng: center.lng }}
        defaultZoom={zoom}
        colorScheme={ColorScheme.DARK}
        gestureHandling="greedy"
        disableDefaultUI
        style={{ width: "100%", height: "100%" }}
      >
        <GoogleOverlays center={center} zoom={zoom} route={route} markers={markers} pulse={pulse} />
      </Map>
    </APIProvider>
  );
}

function GoogleOverlays({
  center,
  zoom,
  route,
  markers,
  pulse,
}: {
  center: LatLng;
  zoom: number;
  route: LatLng[];
  markers: MapMarker[];
  pulse: LatLng | null;
}) {
  const map = useMap();
  const userMoved = useRef(false);
  const seenCamera = useRef("");
  useEffect(() => {
    if (!map || !window.google?.maps) return;
    const maps = window.google?.maps;
    if (!maps) return;
    const drawn: google.maps.Marker[] = markers.map((marker) => new maps.Marker({
      map,
      position: { lat: marker.lat, lng: marker.lng },
      icon: marker.kind === "dropoff"
        ? { path: maps.SymbolPath.CIRCLE, scale: 7, fillColor: "#111", fillOpacity: 1, strokeColor: "#fff", strokeWeight: 2 }
        : {
            url: `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(marker.kind === "bike" ? bikeMarkerSvgFor(marker.heading ?? 0) : personMarkerSvg)}`,
            scaledSize: new maps.Size(36, 36),
            anchor: new maps.Point(18, 18),
          },
    }));
    const line = new maps.Polyline({
      map,
      path: route.map((point) => ({ lat: point.lat, lng: point.lng })),
      strokeColor: "#ffffff",
      strokeWeight: 4,
    });
    const pulseCircle = pulse
      ? new maps.Circle({
          map,
          center: { lat: pulse.lat, lng: pulse.lng },
          radius: 80,
          strokeColor: "#ffffff",
          strokeOpacity: 0.85,
          strokeWeight: 2,
          fillOpacity: 0,
        })
      : null;
    const bounds = new maps.LatLngBounds();
    const points = [...route, ...markers];
    if (pulse) points.push(pulse);
    if (points.length === 0) points.push(center);
    let framing = false;
    const zoomed = map.addListener("zoom_changed", () => {
      if (framing) {
        framing = false;
        return;
      }
      userMoved.current = true;
    });
    const key = points.map((point) => `${point.lat.toFixed(5)},${point.lng.toFixed(5)}`).join(";");
    if (!userMoved.current && key !== seenCamera.current) {
      seenCamera.current = key;
      framing = true;
      if (points.length < 2) {
        const only = points[0] ?? center;
        map.setCenter(only);
        map.setZoom(zoom);
      } else {
        for (const point of points) bounds.extend(point);
        if (!bounds.isEmpty()) map.fitBounds(bounds, { top: 80, right: 48, bottom: 280, left: 48 });
      }
    }
    const drag = map.addListener("dragstart", () => {
      userMoved.current = true;
    });
    return () => {
      for (const marker of drawn) marker.setMap(null);
      line.setMap(null);
      pulseCircle?.setMap(null);
      drag.remove();
      zoomed.remove();
    };
  }, [map, center, zoom, route, markers, pulse]);
  return null;
}
