// Unit tests of the pure server code. Run: npx deno test supabase/functions/tests
import { assert, assertEquals, assertFalse } from "jsr:@std/assert@1";
import { readAssertion, STEAM_OPENID, steamLoginUrl, verifyWithSteam, playerSummary } from "../_shared/steam.ts";
import { LIMITS, validateStart, validateSync } from "../_shared/validate.ts";
import { base64Url, randomToken, safeEqual, sha256Base64Url } from "../_shared/crypto.ts";

const RETURN_TO = "https://x.supabase.co/functions/v1/auth-steam/callback?pending=abc";

function assertion(overrides: Record<string, string> = {}): URLSearchParams {
  return new URLSearchParams({
    "openid.ns": "http://specs.openid.net/auth/2.0",
    "openid.mode": "id_res",
    "openid.op_endpoint": STEAM_OPENID,
    "openid.claimed_id": "https://steamcommunity.com/openid/id/76561197960435530",
    "openid.identity": "https://steamcommunity.com/openid/id/76561197960435530",
    "openid.return_to": RETURN_TO,
    "openid.response_nonce": "2026-10-04T10:00:00Zabc",
    "openid.assoc_handle": "1234567890",
    "openid.signed": "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
    "openid.sig": "c2lnbmF0dXJl",
    ...overrides,
  });
}

Deno.test("steam: login url asks for identifier_select and returns to us", () => {
  const url = new URL(steamLoginUrl(RETURN_TO, "https://x.supabase.co"));
  assertEquals(url.origin + url.pathname, STEAM_OPENID);
  assertEquals(url.searchParams.get("openid.return_to"), RETURN_TO);
  assertEquals(url.searchParams.get("openid.mode"), "checkid_setup");
});

Deno.test("steam: a well-formed assertion yields the SteamID64", () => {
  assertEquals(readAssertion(assertion(), RETURN_TO), { ok: true, steamId: "76561197960435530" });
});

Deno.test("steam: cancel is reported as cancelled", () => {
  assertEquals(readAssertion(new URLSearchParams({ "openid.mode": "cancel" }), RETURN_TO), { ok: false, reason: "cancelled" });
});

Deno.test("steam: forged or replayed assertions are rejected", () => {
  const bad = [
    assertion({ "openid.return_to": "https://x.supabase.co/functions/v1/auth-steam/callback?pending=other" }),
    assertion({ "openid.op_endpoint": "https://evil.example/openid/login" }),
    assertion({ "openid.claimed_id": "https://evil.example/openid/id/76561197960435530" }),
    assertion({ "openid.identity": "https://steamcommunity.com/openid/id/76561197960435531" }),
    assertion({ "openid.claimed_id": "https://steamcommunity.com/openid/id/123", "openid.identity": "https://steamcommunity.com/openid/id/123" }),
    assertion({ "openid.signed": "signed,op_endpoint,claimed_id,identity" }),
    assertion({ "openid.mode": "checkid_setup" }),
  ];
  for (const params of bad) assertEquals(readAssertion(params, RETURN_TO).ok, false);
});

Deno.test("steam: direct verification posts check_authentication and reads is_valid", async () => {
  let sent: URLSearchParams | null = null;
  const fake = (async (_url: string, init?: RequestInit) => {
    sent = init?.body as URLSearchParams;
    return new Response("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n");
  }) as typeof fetch;
  assert(await verifyWithSteam(assertion({ unrelated: "x" }), fake));
  assertEquals(sent!.get("openid.mode"), "check_authentication");
  assertEquals(sent!.get("openid.sig"), "c2lnbmF0dXJl");
  assertEquals(sent!.get("unrelated"), null);

  const no = (async () => new Response("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n")) as typeof fetch;
  assertFalse(await verifyWithSteam(assertion(), no));
});

Deno.test("steam: player summary keeps only https urls and valid countries", async () => {
  const fake = (async () => Response.json({
    response: { players: [{ personaname: "zegolka", avatarfull: "https://avatars.steamstatic.com/a.jpg", profileurl: "http://steamcommunity.com/id/z/", loccountrycode: "MD" }] },
  })) as typeof fetch;
  const player = await playerSummary("76561197960435530", "key", fake);
  assertEquals(player, { personaName: "zegolka", avatarUrl: "https://avatars.steamstatic.com/a.jpg", profileUrl: "", countryCode: "MD" });
  const none = (async () => Response.json({ response: { players: [] } })) as typeof fetch;
  assertEquals(await playerSummary("76561197960435530", "key", none), null);
});

const NOW = new Date("2026-10-04T12:00:00Z");
const PC = "6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
const row = (overrides: Record<string, unknown> = {}) => ({
  day: "2026-10-03", gameKey: "steam:730", mouseKey: "a1b2c3", centimeters: 12_345.6, clicks: 1200, moveSeconds: 3600, peakSpeed: 250, ...overrides,
});

Deno.test("sync: accepts a normal upload", () => {
  const v = validateSync({ pcId: PC, pcName: "PC", rows: [row(), row({ gameKey: "" })] }, NOW);
  assert(v.ok);
  assertEquals(v.request.rows.length, 2);
  assertEquals(v.rejected, 0);
});

Deno.test("sync: malformed requests fail as a whole", () => {
  for (const body of [null, "x", { pcId: "nope", rows: [] }, { pcId: PC }, { pcId: PC, rows: new Array(LIMITS.maxRows + 1).fill(row()) }]) {
    assertFalse(validateSync(body, NOW).ok);
  }
});

Deno.test("sync: implausible rows are dropped and counted", () => {
  const v = validateSync({
    pcId: PC,
    rows: [
      row(),
      row({ day: "2026-10-06" }), // future
      row({ day: "2019-12-31" }), // before the app existed
      row({ day: "2026-02-30" }), // not a date
      row({ centimeters: -1 }),
      row({ centimeters: Number.NaN }),
      row({ clicks: 1.5 }),
      row({ peakSpeed: 9_000 }),
      row({ gameKey: "<script>" }),
      row({ mouseKey: "XYZ" }),
      row(), // duplicate of the first
    ],
  }, NOW);
  assert(v.ok);
  assertEquals(v.request.rows.length, 1);
  assertEquals(v.rejected, 10);
});

Deno.test("sync: a day that is impossible as a whole is dropped entirely", () => {
  const v = validateSync({
    pcId: PC,
    rows: [
      row({ gameKey: "a", centimeters: 6_000_000 }),
      row({ gameKey: "b", centimeters: 6_000_000 }), // 120 km on one day
      row({ day: "2026-10-02" }),
    ],
  }, NOW);
  assert(v.ok);
  assertEquals(v.request.rows.map((r) => r.day), ["2026-10-02"]);
  assertEquals(v.rejected, 2);
});

Deno.test("sync: tomorrow (UTC) is allowed for far-east time zones", () => {
  const v = validateSync({ pcId: PC, rows: [row({ day: "2026-10-05" })] }, NOW);
  assert(v.ok);
  assertEquals(v.rejected, 0);
});

Deno.test("start: parameters are validated", () => {
  const good = new URLSearchParams({ challenge: "a".repeat(43), port: "51234", state: "s".repeat(32) });
  assertEquals(validateStart(good), { challenge: "a".repeat(43), port: 51234, state: "s".repeat(32) });
  for (const [k, v] of [["challenge", "short"], ["port", "80"], ["port", "x"], ["state", "bad state with spaces"]]) {
    const p = new URLSearchParams(good);
    p.set(k, v);
    assertEquals(validateStart(p), null);
  }
});

Deno.test("crypto: S256 matches the PKCE reference vector", async () => {
  // RFC 7636, appendix B.
  assertEquals(await sha256Base64Url("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"), "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
  assertEquals(base64Url(new Uint8Array([251, 255])), "-_8");
  assertEquals(randomToken().length, 43);
  assert(safeEqual("abc", "abc"));
  assertFalse(safeEqual("abc", "abd"));
  assertFalse(safeEqual("abc", "ab"));
});
