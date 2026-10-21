import { apiOrigin, appAccessToken, settingsFromEnv } from "@/shared/api/appToken";
import { personaCookieName, sealPersona } from "@/shared/api/persona";

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
  if (!list.some((user) => user.id === userId)) return Response.json({ error: "Unknown user" }, { status: 404 });

  const secure = new URL(request.url).protocol === "https:" ? "; Secure" : "";
  return new Response(null, {
    status: 204,
    headers: {
      "Set-Cookie": `${personaCookieName}=${sealPersona(userId, settings.clientSecret)}; HttpOnly; Path=/; Max-Age=28800; SameSite=Lax${secure}`,
    },
  });
}
