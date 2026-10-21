import { appAccessToken, settingsFromEnv } from "@/shared/api/appToken";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(): Promise<Response> {
  const settings = settingsFromEnv();
  if (!settings) return Response.json({ token: null });
  try {
    return Response.json({ token: await appAccessToken(settings) });
  } catch {
    return Response.json({ error: "The API token could not be acquired." }, { status: 502 });
  }
}
