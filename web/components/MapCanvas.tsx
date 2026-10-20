"use client";

import { useEffect, useState } from "react";
import type { LatLng } from "@/lib/types";

export type MapMarker = LatLng & {
  kind: "pickup" | "dropoff" | "bike";
  heading?: number;
};

type Size = { width: number; height: number };

export function MapCanvas({
  center,
  route = [],
  markers = [],
  pulse = null,
}: {
  center: LatLng;
  route?: LatLng[];
  markers?: MapMarker[];
  pulse?: LatLng | null;
}) {
  const [host, setHost] = useState<HTMLDivElement | null>(null);
  const [size, setSize] = useState<Size>({ width: 1000, height: 800 });

  useEffect(() => {
    if (!host) return;
    const update = () => {
      const rect = host.getBoundingClientRect();
      setSize({
        width: Math.max(320, rect.width),
        height: Math.max(320, rect.height),
      });
    };
    update();
    const observer = new ResizeObserver(update);
    observer.observe(host);
    return () => observer.disconnect();
  }, [host]);

  const points = [...route, ...markers];
  const project = makeProjection(points.length > 0 ? points : [center], size);
  const line = route.map(project);
  const pulsePoint = pulse ? project(pulse) : null;

  return (
    <div className="map-canvas" ref={setHost} aria-hidden="true">
      <svg viewBox={`0 0 ${size.width} ${size.height}`} preserveAspectRatio="xMidYMid slice">
        <rect width={size.width} height={size.height} fill="#16181c" />
        <StreetGrid width={size.width} height={size.height} />
        {pulsePoint ? (
          <g>
            <circle cx={pulsePoint.x} cy={pulsePoint.y} r="28" fill="none" stroke="#3a3d44" strokeWidth="2" />
            <circle cx={pulsePoint.x} cy={pulsePoint.y} r="52" fill="none" stroke="#2a2d33" strokeWidth="2" />
            <circle cx={pulsePoint.x} cy={pulsePoint.y} r="78" fill="none" stroke="#22252b" strokeWidth="2" />
          </g>
        ) : null}
        {line.length >= 2 ? (
          <g>
            <polyline
              points={toPoints(line)}
              fill="none"
              stroke="#3a3d44"
              strokeWidth="10"
              strokeLinejoin="round"
              strokeLinecap="round"
            />
            <polyline
              points={toPoints(line)}
              fill="none"
              stroke="#ffffff"
              strokeWidth="4"
              strokeLinejoin="round"
              strokeLinecap="round"
            />
          </g>
        ) : null}
        {markers.map((marker, index) => {
          const point = project(marker);
          if (marker.kind === "bike") {
            return <BikeGlyph key={`bike-${index}`} x={point.x} y={point.y} heading={marker.heading ?? 0} />;
          }
          if (marker.kind === "dropoff") {
            return (
              <rect
                key={`drop-${index}`}
                x={point.x - 7}
                y={point.y - 7}
                width="14"
                height="14"
                rx="2"
                fill="#fff"
              />
            );
          }
          return (
            <g key={`pick-${index}`}>
              <circle cx={point.x} cy={point.y} r="9" fill="#fff" />
              <circle cx={point.x} cy={point.y} r="3.5" fill="#000" />
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function StreetGrid({ width, height }: Size) {
  const lines = [];
  const step = 56;
  let column = 0;
  for (let x = 0; x <= width; x += step) {
    const major = column % 4 === 0;
    lines.push(
      <line
        key={`v-${column}`}
        x1={x}
        y1={0}
        x2={x}
        y2={height}
        stroke={major ? "#101114" : "#13161a"}
        strokeWidth={major ? 3 : 1}
      />,
    );
    column += 1;
  }
  let row = 0;
  for (let y = 0; y <= height; y += step) {
    const major = row % 4 === 0;
    lines.push(
      <line
        key={`h-${row}`}
        x1={0}
        y1={y}
        x2={width}
        y2={y}
        stroke={major ? "#101114" : "#13161a"}
        strokeWidth={major ? 3 : 1}
      />,
    );
    row += 1;
  }
  return <g>{lines}</g>;
}

function BikeGlyph({ x, y, heading }: { x: number; y: number; heading: number }) {
  return (
    <g transform={`translate(${x} ${y}) rotate(${heading})`}>
      <circle r="16" fill="#000" stroke="#fff" strokeWidth="2" />
      <circle cx="0" cy="-5" r="3.1" fill="none" stroke="#fff" strokeWidth="1.6" />
      <circle cx="0" cy="6" r="3.1" fill="none" stroke="#fff" strokeWidth="1.6" />
      <path d="M0 -5 L4 1.2 L0 6 L-3.4 1.2 Z" fill="none" stroke="#fff" strokeWidth="1.5" />
      <path d="M0 -16 L5 -8 H-5 Z" fill="#fff" />
    </g>
  );
}

function toPoints(points: Array<{ x: number; y: number }>): string {
  return points.map((point) => `${point.x},${point.y}`).join(" ");
}

function makeProjection(points: LatLng[], size: Size) {
  const safe = points.filter((point) => Number.isFinite(point.lat) && Number.isFinite(point.lng));
  const source = safe.length > 0 ? safe : [{ lat: 10.77254, lng: 106.698 }];
  const midLat0 = average(source.map((point) => point.lat));
  const midLng0 = average(source.map((point) => point.lng));
  const latSpan = Math.max(span(source.map((point) => point.lat)), 0.012);
  const lngSpan = Math.max(span(source.map((point) => point.lng)), 0.012);
  const minLat = midLat0 - latSpan / 2 - latSpan * 0.22;
  const maxLat = midLat0 + latSpan / 2 + latSpan * 0.22;
  const minLng = midLng0 - lngSpan / 2 - lngSpan * 0.22;
  const maxLng = midLng0 + lngSpan / 2 + lngSpan * 0.22;
  const midLat = (minLat + maxLat) / 2;
  const cos = Math.max(0.2, Math.cos((midLat * Math.PI) / 180));
  const worldW = Math.max(0.00001, (maxLng - minLng) * cos);
  const worldH = Math.max(0.00001, maxLat - minLat);
  const left = size.width * 0.1;
  const right = size.width * 0.9;
  const top = size.height * 0.1;
  const bottom = size.height - Math.max(200, size.height * 0.38);
  const drawW = Math.max(1, right - left);
  const drawH = Math.max(1, bottom - top);
  const scale = Math.min(drawW / worldW, drawH / worldH);
  const usedW = worldW * scale;
  const usedH = worldH * scale;
  const originX = left + (drawW - usedW) / 2;
  const originY = top + (drawH - usedH) / 2;

  return (point: LatLng) => ({
    x: originX + (point.lng - minLng) * cos * scale,
    y: originY + (maxLat - point.lat) * scale,
  });
}

function average(values: number[]): number {
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

function span(values: number[]): number {
  return Math.max(...values) - Math.min(...values);
}
