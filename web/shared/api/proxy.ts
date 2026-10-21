import { apiOrigin, appAccessToken, settingsFromEnv } from "./appToken";
import { personaFromCookieHeader } from "./persona";

const REQUEST_SKIP = new Set([
  "host",
  "connection",
  "content-length",
  "transfer-encoding",
  "keep-alive",
  "upgrade",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "authorization",
  "cookie",
]);

const RESPONSE_SKIP = new Set(["transfer-encoding", "content-encoding", "content-length", "connection", "set-cookie"]);

export function isForwardedPath(path: string): boolean {
  return path === "/api" || path.startsWith("/api/") || path === "/hubs" || path.startsWith("/hubs/");
}

export function forwardedHeaders(incoming: Headers, token: string | null): Headers {
  const headers = new Headers();
  incoming.forEach((value, key) => {
    if (REQUEST_SKIP.has(key.toLowerCase())) return;
    headers.append(key, value);
  });
  if (token) headers.set("authorization", `Bearer ${token}`);
  return headers;
}

export async function proxyApi(request: Request, segments: string[]): Promise<Response> {
  if (segments.length === 0 || segments.some((segment) => segment === "" || segment === "." || segment === ".." || segment.includes("/")))
    return new Response(null, { status: 404 });

  const path = `/${segments.join("/")}`;
  if (!isForwardedPath(path)) return new Response(null, { status: 404 });

  const settings = settingsFromEnv();
  let token: string | null = null;
  const headers = forwardedHeaders(request.headers, null);
  if (settings) {
    const actor = personaFromCookieHeader(request.headers.get("cookie"), settings.clientSecret);
    if (!actor) return new Response(null, { status: 401 });
    headers.set("x-user-id", actor);
    try {
      token = await appAccessToken(settings);
    } catch {
      return Response.json({ error: "The API token could not be acquired." }, { status: 502 });
    }
    headers.set("authorization", `Bearer ${token}`);
  }

  const upstreamUrl = new URL(`${apiOrigin()}${path}${new URL(request.url).search}`);
  const actor = headers.get("x-user-id");
  if (settings && actor && upstreamUrl.searchParams.has("userId")) upstreamUrl.searchParams.set("userId", actor);
  const init: RequestInit & { duplex?: "half" } = {
    method: request.method,
    headers,
    redirect: "manual",
    signal: request.signal,
  };
  if (request.method !== "GET" && request.method !== "HEAD") {
    init.body = request.body;
    init.duplex = "half";
  }

  let upstream: Response;
  try {
    upstream = await fetch(upstreamUrl, init);
  } catch (error) {
    if (request.signal.aborted) return new Response(null, { status: 499 });
    throw error;
  }
  const responseHeaders = new Headers();
  upstream.headers.forEach((value, key) => {
    if (RESPONSE_SKIP.has(key.toLowerCase())) return;
    responseHeaders.append(key, value);
  });
  return new Response(upstream.body, { status: upstream.status, headers: responseHeaders });
}
