export type EntraServerSettings = {
  tenantId: string;
  clientId: string;
  clientSecret: string;
  scope: string;
};

type CachedToken = {
  key: string;
  token: string;
  expiresAt: number;
};

let cached: CachedToken | null = null;
let inflight: Promise<string> | null = null;
let inflightKey = "";

export function clearAppTokenCache(): void {
  cached = null;
  inflight = null;
  inflightKey = "";
}

export function settingsFromEnv(env: NodeJS.ProcessEnv = process.env): EntraServerSettings | null {
  const tenantId = env.AZURE_TENANT_ID?.trim() ?? "";
  const clientId = env.AZURE_CLIENT_ID?.trim() ?? "";
  const clientSecret = env.AZURE_CLIENT_SECRET?.trim() ?? "";
  const audience = env.AZURE_API_CLIENT_ID?.trim() || clientId;
  const scope = env.AZURE_API_SCOPE?.trim() || (audience ? `api://${audience}/.default` : "");
  if (!tenantId || !clientId || !clientSecret || !scope) return null;
  return { tenantId, clientId, clientSecret, scope };
}

export function apiOrigin(env: NodeJS.ProcessEnv = process.env): string {
  const configured = env.API_BASE_URL?.trim() || env.NEXT_PUBLIC_API_BASE_URL?.trim();
  const base = configured && configured.length > 0 ? configured : "http://localhost:5080";
  return base.replace(/\/+$/, "");
}

export async function appAccessToken(
  settings: EntraServerSettings,
  fetchImpl: typeof fetch = fetch,
  now: () => number = Date.now,
): Promise<string> {
  const key = `${settings.tenantId}|${settings.clientId}|${settings.scope}`;
  if (cached && cached.key === key && cached.expiresAt > now()) return cached.token;
  if (inflight && inflightKey === key) return inflight;

  inflightKey = key;
  inflight = requestToken(settings, fetchImpl, now, key).finally(() => {
    inflight = null;
  });
  return inflight;
}

async function requestToken(
  settings: EntraServerSettings,
  fetchImpl: typeof fetch,
  now: () => number,
  key: string,
): Promise<string> {
  const body = new URLSearchParams({
    client_id: settings.clientId,
    client_secret: settings.clientSecret,
    scope: settings.scope,
    grant_type: "client_credentials",
  });
  const response = await fetchImpl(`https://login.microsoftonline.com/${settings.tenantId}/oauth2/v2.0/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });
  const payload = (await response.json().catch(() => null)) as { access_token?: string; expires_in?: number; error?: string } | null;
  if (!response.ok || !payload?.access_token) {
    throw new Error(payload?.error || "Entra rejected the application token request");
  }

  const lifetimeMs = Math.max((payload.expires_in ?? 300) - 60, 1) * 1000;
  cached = { key, token: payload.access_token, expiresAt: now() + lifetimeMs };
  return payload.access_token;
}
