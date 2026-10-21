import { captureUserId } from "@/features/identity/storage";
import { apiBase } from "./base";
import { getActivityClient } from "../telemetry/browser";
import type { Correlation } from "../telemetry/activity";
export { apiBase } from "./base";

export class ApiError extends Error {
  readonly status: number;
  readonly payload: unknown;

  constructor(status: number, payload: unknown) {
    super(readApiMessage(payload) ?? `Request failed (${status})`);
    this.name = "ApiError";
    this.status = status;
    this.payload = payload;
  }
}

export function readApiMessage(payload: unknown): string | null {
  if (typeof payload === "string" && payload.trim()) return payload.trim();
  if (!payload || typeof payload !== "object") return null;
  const body = payload as Record<string, unknown>;
  if (typeof body.reason === "string" && body.reason.trim()) return body.reason.trim();
  if (body.error === "ActiveRide") return "You already have an active ride";
  if (typeof body.error === "string" && body.error.trim()) return body.error.trim();
  return null;
}

export function messageFrom(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof TypeError) return "Can't reach the server";
  if (error instanceof Error && error.message) return error.message;
  return "Something went wrong";
}

type RequestOptions = {
  method?: string;
  userId?: string | null;
  body?: unknown;
  telemetry?: Correlation;
};

let sessionUser: string | null = null;

export async function ensurePersonaSession(userId: string): Promise<void> {
  if (typeof window === "undefined" || sessionUser === userId) return;
  const response = await fetch("/backend/session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId }),
  });
  if (!response.ok) {
    const text = await response.text();
    let payload: unknown = null;
    if (text) {
      try {
        payload = JSON.parse(text) as unknown;
      } catch {
        payload = { error: text };
      }
    }
    throw new ApiError(response.status, payload);
  }
  sessionUser = userId;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const actor = options.userId ?? (typeof window === "undefined" ? null : captureUserId());
  if (actor) await ensurePersonaSession(actor);
  const headers = new Headers();
  if (options.userId) headers.set("X-User-Id", options.userId);
  const telemetry = options.telemetry ?? getActivityClient(options.userId)?.correlation();
  if (telemetry) {
    headers.set("X-App-Session-Id", telemetry.appSessionId);
    if (telemetry.activityId) headers.set("X-Activity-Id", telemetry.activityId);
    if (telemetry.rideId) headers.set("X-Ride-Id", telemetry.rideId);
  }
  let body: string | undefined;
  if (options.body !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(options.body);
  }

  const response = await fetch(`${apiBase()}${path}`, {
    method: options.method ?? "GET",
    headers,
    body,
    cache: "no-store",
  });

  if (response.status === 204 || response.status === 205) return null as T;
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      payload = { error: text };
    }
  }
  if (!response.ok) throw new ApiError(response.status, payload);
  return payload as T;
}
