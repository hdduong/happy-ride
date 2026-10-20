"use client";

import { useEffect } from "react";
import { APIProvider, Map, useMap, ColorScheme } from "@vis.gl/react-google-maps";
import type { LatLng, MapMarker } from "./types";

export function GoogleDarkMap({
  apiKey,
  region,
  language,
  center,
  route,
  markers,
  pulse,
}: {
  apiKey: string;
  region: string;
  language: string;
  center: LatLng;
  route: LatLng[];
  markers: MapMarker[];
  pulse: LatLng | null;
}) {
  return (
    <APIProvider apiKey={apiKey} region={region} language={language}>
      <Map
        defaultCenter={{ lat: center.lat, lng: center.lng }}
        defaultZoom={15}
        colorScheme={ColorScheme.DARK}
        gestureHandling="greedy"
        disableDefaultUI
        style={{ width: "100%", height: "100%" }}
      >
        <GoogleOverlays center={center} route={route} markers={markers} pulse={pulse} />
      </Map>
    </APIProvider>
  );
}

function GoogleOverlays({
  center,
  route,
  markers,
  pulse,
}: {
  center: LatLng;
  route: LatLng[];
  markers: MapMarker[];
  pulse: LatLng | null;
}) {
  const map = useMap();
  useEffect(() => {
    if (!map || !window.google?.maps) return;
    const maps = window.google?.maps;
    if (!maps) return;
    const drawn: google.maps.Marker[] = markers.map((marker) => new maps.Marker({
      map,
      position: { lat: marker.lat, lng: marker.lng },
      icon: marker.kind === "bike"
        ? { path: maps.SymbolPath.FORWARD_CLOSED_ARROW, scale: 5, fillColor: "#fff", fillOpacity: 1, strokeWeight: 1, rotation: marker.heading ?? 0 }
        : marker.kind === "dropoff"
          ? { path: maps.SymbolPath.CIRCLE, scale: 7, fillColor: "#fff", fillOpacity: 1, strokeColor: "#000", strokeWeight: 2 }
          : undefined,
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
    if (points.length < 2) {
      const only = points[0] ?? center;
      map.setCenter(only);
      map.setZoom(15);
    } else {
      for (const point of points) bounds.extend(point);
      if (!bounds.isEmpty()) map.fitBounds(bounds, 80);
    }
    return () => {
      for (const marker of drawn) marker.setMap(null);
      line.setMap(null);
      pulseCircle?.setMap(null);
    };
  }, [map, center, route, markers, pulse]);
  return null;
}
