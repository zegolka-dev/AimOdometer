// Steam sign-in (OpenID 2.0) and the Steam Web API. Steam is not an OIDC provider, so Supabase Auth cannot use it
// directly: the auth-steam function verifies the OpenID assertion here and then creates the Supabase session itself.

export const STEAM_OPENID = "https://steamcommunity.com/openid/login";
const OPENID_NS = "http://specs.openid.net/auth/2.0";
const IDENTIFIER_SELECT = "http://specs.openid.net/auth/2.0/identifier_select";
const CLAIMED_ID = /^https:\/\/steamcommunity\.com\/openid\/id\/(\d{17})$/;

/** Where to send the browser to sign in with Steam. */
export function steamLoginUrl(returnTo: string, realm: string): string {
  const params = new URLSearchParams({
    "openid.ns": OPENID_NS,
    "openid.mode": "checkid_setup",
    "openid.return_to": returnTo,
    "openid.realm": realm,
    "openid.identity": IDENTIFIER_SELECT,
    "openid.claimed_id": IDENTIFIER_SELECT,
  });
  return `${STEAM_OPENID}?${params}`;
}

export type Assertion =
  | { ok: true; steamId: string }
  | { ok: false; reason: "cancelled" | "invalid" };

/**
 * Structural checks of Steam's answer before asking Steam to confirm it: positive assertion, from Steam's endpoint,
 * for exactly our return_to (so it cannot be replayed for another sign-in), with a well-formed SteamID64.
 */
export function readAssertion(params: URLSearchParams, expectedReturnTo: string): Assertion {
  const mode = params.get("openid.mode");
  if (mode === "cancel") return { ok: false, reason: "cancelled" };
  if (mode !== "id_res" || params.get("openid.ns") !== OPENID_NS) return { ok: false, reason: "invalid" };
  if (params.get("openid.op_endpoint") !== STEAM_OPENID) return { ok: false, reason: "invalid" };
  if (params.get("openid.return_to") !== expectedReturnTo) return { ok: false, reason: "invalid" };
  const claimed = params.get("openid.claimed_id") ?? "";
  if (params.get("openid.identity") !== claimed) return { ok: false, reason: "invalid" };
  const match = CLAIMED_ID.exec(claimed);
  if (!match) return { ok: false, reason: "invalid" };
  const signed = (params.get("openid.signed") ?? "").split(",");
  for (const field of ["op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle"]) {
    if (!signed.includes(field)) return { ok: false, reason: "invalid" };
  }
  return { ok: true, steamId: match[1] };
}

/** Asks Steam whether the assertion is genuine (direct verification, "check_authentication"). */
export async function verifyWithSteam(params: URLSearchParams, fetchImpl: typeof fetch = fetch): Promise<boolean> {
  const body = new URLSearchParams();
  for (const [key, value] of params) {
    if (key.startsWith("openid.")) body.set(key, value);
  }
  body.set("openid.mode", "check_authentication");
  const response = await fetchImpl(STEAM_OPENID, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });
  if (!response.ok) return false;
  const text = await response.text();
  return text.split("\n").some((line) => line.trim() === "is_valid:true");
}

export interface SteamPlayer {
  personaName: string;
  avatarUrl: string;
  profileUrl: string;
  countryCode: string | null;
}

/** Public profile of one player (ISteamUser/GetPlayerSummaries). Null when Steam does not know the id. */
export async function playerSummary(steamId: string, apiKey: string, fetchImpl: typeof fetch = fetch): Promise<SteamPlayer | null> {
  const url = `https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=${encodeURIComponent(apiKey)}&steamids=${steamId}`;
  const response = await fetchImpl(url);
  if (!response.ok) throw new Error(`Steam Web API: HTTP ${response.status}`);
  const data = await response.json();
  const player = data?.response?.players?.[0];
  if (!player) return null;
  const country = typeof player.loccountrycode === "string" && /^[A-Z]{2}$/.test(player.loccountrycode)
    ? player.loccountrycode
    : null;
  return {
    personaName: String(player.personaname ?? "").slice(0, 64),
    avatarUrl: httpsOrEmpty(player.avatarfull ?? player.avatarmedium ?? ""),
    profileUrl: httpsOrEmpty(player.profileurl ?? ""),
    countryCode: country,
  };
}

function httpsOrEmpty(value: unknown): string {
  const text = String(value);
  return text.startsWith("https://") && text.length <= 512 ? text : "";
}
