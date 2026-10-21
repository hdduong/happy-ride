import assert from "node:assert/strict";
import test from "node:test";
import { appAccessToken, clearAppTokenCache, settingsFromEnv } from "../shared/api/appToken.ts";
import { forwardedHeaders, isForwardedPath } from "../shared/api/proxy.ts";
import { openPersona, sealPersona } from "../shared/api/persona.ts";

test("missing Entra settings leave the local demo without a token", () => {
  assert.equal(settingsFromEnv({}), null);
  assert.equal(settingsFromEnv({ AZURE_TENANT_ID: "tenant", AZURE_CLIENT_ID: "client" }), null);
});

test("application settings derive the API scope", () => {
  const settings = settingsFromEnv({
    AZURE_TENANT_ID: "tenant",
    AZURE_CLIENT_ID: "web",
    AZURE_CLIENT_SECRET: "secret",
    AZURE_API_CLIENT_ID: "api",
  });
  assert.deepEqual(settings, {
    tenantId: "tenant",
    clientId: "web",
    clientSecret: "secret",
    scope: "api://api/.default",
  });
});

test("the server caches the application token and replaces any caller token", async () => {
  clearAppTokenCache();
  let calls = 0;
  const fetchImpl = async () => {
    calls += 1;
    return new Response(JSON.stringify({ access_token: "app-token", expires_in: 3600 }), { status: 200 });
  };
  const settings = {
    tenantId: "tenant",
    clientId: "web",
    clientSecret: "secret",
    scope: "api://api/.default",
  };

  const first = await appAccessToken(settings, fetchImpl, () => 1_000);
  const second = await appAccessToken(settings, fetchImpl, () => 1_000);
  assert.equal(first, "app-token");
  assert.equal(second, "app-token");
  assert.equal(calls, 1);

  const headers = forwardedHeaders(new Headers({ Authorization: "Bearer stolen", "X-User-Id": "an" }), first);
  assert.equal(headers.get("authorization"), "Bearer app-token");
  assert.equal(headers.get("x-user-id"), "an");
});

test("a persona cookie is accepted only with the server secret", () => {
  const sealed = sealPersona("11111111-1111-1111-1111-111111111111", "server-secret");
  assert.equal(openPersona(sealed, "server-secret"), "11111111-1111-1111-1111-111111111111");
  assert.equal(openPersona(sealed, "other-secret"), null);
  assert.equal(openPersona("11111111-1111-1111-1111-111111111111.forged", "server-secret"), null);
});

test("the proxy only forwards API and hub paths", () => {
  assert.equal(isForwardedPath("/api/dev/users"), true);
  assert.equal(isForwardedPath("/hubs/ride/negotiate"), true);
  assert.equal(isForwardedPath("/swagger"), false);
});
