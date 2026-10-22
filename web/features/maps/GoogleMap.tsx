"use client";

import { useEffect } from "react";
import { APIProvider, Map, useMap } from "@vis.gl/react-google-maps";
import type { LatLng, MapMarker } from "./types";

export function GoogleMap({
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
        gestureHandling="greedy"
        disableDefaultUI
        clickableIcons={false}
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
  useEffect(() => {
    if (!map || !window.google?.maps) return;
    const maps = window.google.maps;
    const path = route.map((point) => ({ lat: point.lat, lng: point.lng }));
    const drawn = markers.map((marker) => new maps.Marker({
      map,
      position: { lat: marker.lat, lng: marker.lng },
      icon: marker.kind === "bike"
        ? { path: maps.SymbolPath.FORWARD_CLOSED_ARROW, scale: 5, fillColor: "#111", fillOpacity: 1, strokeColor: "#fff", strokeWeight: 1, rotation: marker.heading ?? 0 }
        : marker.kind === "dropoff"
          ? { path: maps.SymbolPath.CIRCLE, scale: 7, fillColor: "#111", fillOpacity: 1, strokeColor: "#fff", strokeWeight: 2 }
          : { path: maps.SymbolPath.CIRCLE, scale: 7, fillColor: "#fff", fillOpacity: 1, strokeColor: "#111", strokeWeight: 3 },
    }));
    const casing = new maps.Polyline({ map, path, strokeColor: "#ffffff", strokeWeight: 8 });
    const line = new maps.Polyline({ map, path, strokeColor: "#111111", strokeWeight: 4 });
    const pulseCircle = pulse
      ? new maps.Circle({
          map,
          center: { lat: pulse.lat, lng: pulse.lng },
          radius: 80,
          strokeColor: "#111111",
          strokeOpacity: 0.85,
          strokeWeight: 2,
          fillOpacity: 0,
        })
      : null;
    const bounds = new maps.LatLngBounds();
    const points = [...route, ...markers];
    if (pulse) points.push(pulse);
    if (points.length === 0) points.push(center);
    if (points.length < 2) {
      const only = points[0] ?? center;
      map.setCenter(only);
      map.setZoom(zoom);
    } else {
      for (const point of points) bounds.extend(point);
      if (!bounds.isEmpty()) map.fitBounds(bounds, { top: 80, right: 48, bottom: 280, left: 48 });
    }
    return () => {
      for (const marker of drawn) marker.setMap(null);
      casing.setMap(null);
      line.setMap(null);
      pulseCircle?.setMap(null);
    };
  }, [map, center, zoom, route, markers, pulse]);
  return null;
}
