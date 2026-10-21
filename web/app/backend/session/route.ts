import { apiOrigin, appAccessToken, settingsFromEnv } from "@/shared/api/appToken";
import { sealPersona } from "@/shared/api/persona";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

const userIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function POST(request: Request): Promise<Response> {
  const settings = settingsFromEnv();
  if (!settings) return new Response(null, { status: 204 });

  const body = (await request.json().catch(() => null)) as { userId?: unknown } | null;
  const userId = typeof body?.userId === "string" ? body.userId : "";
  if (!userIdPattern.test(userId)) return Response.json({ error: "Unknown user" }, { status: 400 });

  let token: string;
  try {
    token = await appAccessToken(settings);
  } catch {
    return Response.json({ error: "The API token could not be acquired." }, { status: 502 });
  }

  const users = await fetch(`${apiOrigin()}/api/dev/users`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });
  if (!users.ok) return Response.json({ error: "The user list could not be checked." }, { status: 502 });
  const list = (await users.json()) as { id?: string }[];
  const match = list.find((user) => user.id?.toLowerCase() === userId.toLowerCase());
  if (!match?.id) return Response.json({ error: "Unknown user" }, { status: 404 });

  const expiresAt = Math.floor(Date.now() / 1000) + 8 * 60 * 60;
  return Response.json({ proof: sealPersona(match.id, settings.clientSecret, expiresAt) });
}
