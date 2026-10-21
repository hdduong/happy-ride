import assert from "node:assert/strict";
import test from "node:test";
import { appAccessToken, clearAppTokenCache, settingsFromEnv } from "../shared/api/appToken.ts";
import { allowsAnonymousBootstrap, bindPersonaQuery, forwardedHeaders, isForwardedPath } from "../shared/api/proxy.ts";
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

test("a persona proof is accepted only with the server secret and before it expires", () => {
  const id = "11111111-1111-1111-1111-111111111111";
  const sealed = sealPersona(id, "server-secret", Math.floor(Date.now() / 1000) + 3600);
  assert.equal(openPersona(sealed, "server-secret"), id);
  assert.equal(openPersona(sealed, "other-secret"), null);
  assert.equal(openPersona(sealPersona(id, "server-secret", Math.floor(Date.now() / 1000) - 5), "server-secret"), null);
});

test("the proxy only forwards API and hub paths", () => {
  assert.equal(isForwardedPath("/api/dev/users"), true);
  assert.equal(isForwardedPath("/hubs/ride/negotiate"), true);
  assert.equal(isForwardedPath("/swagger"), false);
});

test("the hub query keeps the sealed persona and drops any other user id key", () => {
  const actor = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  const url = new URL(`http://api/hubs/ride?UserId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb&userId=cccccccc-cccc-cccc-cccc-cccccccccccc&appSessionId=s`);
  bindPersonaQuery(url, actor);
  const userKeys = [...url.searchParams.keys()].filter((key) => key.toLowerCase() === "userid");
  assert.deepEqual(userKeys, ["userId"]);
  assert.equal(url.searchParams.get("userId"), actor);
  assert.equal(url.searchParams.get("appSessionId"), "s");
});

test("only the user list can be read before a persona cookie exists", () => {
  assert.equal(allowsAnonymousBootstrap("GET", "/api/dev/users"), true);
  assert.equal(allowsAnonymousBootstrap("POST", "/api/dev/users"), false);
  assert.equal(allowsAnonymousBootstrap("GET", "/api/rides/current"), false);
  assert.equal(allowsAnonymousBootstrap("GET", "/hubs/ride"), false);
});
