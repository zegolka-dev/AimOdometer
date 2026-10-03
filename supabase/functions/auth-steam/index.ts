// Sign-in with Steam for the AimOdometer window (a native app), in three steps:
//
//   GET  /start?challenge&port&state  The app opened this in the browser. We remember the handshake (challenge =
//                                      base64url(SHA-256(verifier)), the app's loopback port and its state) and send
//                                      the browser to Steam.
//   GET  /callback?pending            Steam sends the browser back. We verify the OpenID assertion with Steam, create
//                                      or find the user, and send the browser to http://127.0.0.1:<port>/callback with a
//                                      one-time code. Only the app listening on that port receives it.
//   POST /exchange {code, verifier}    The app trades code + verifier for a real Supabase session (access + refresh
//                                      token). Without the verifier, which never left the app, a stolen code is useless.
//
// The Steam Web API key is only read here, from the function's secrets.
import { admin, allow, anonClient, clientIp, error, json, SUPABASE_URL } from "../_shared/server.ts";
import { playerSummary, readAssertion, steamLoginUrl, verifyWithSteam } from "../_shared/steam.ts";
import { randomToken, safeEqual, sha256Base64Url } from "../_shared/crypto.ts";
import { validateStart } from "../_shared/validate.ts";

const BASE = `${SUPABASE_URL}/functions/v1/auth-steam`;
const SERVICE_EMAIL_DOMAIN = "users.aimodometer.invalid"; // never receives mail; email sign-up is disabled

Deno.serve(async (req) => {
  const url = new URL(req.url);
  const route = url.pathname.split("/").filter(Boolean).pop();
  try {
    if (req.method === "GET" && route === "start") return await start(req, url.searchParams);
    if (req.method === "GET" && route === "callback") return await callback(url.searchParams);
    if (req.method === "POST" && route === "exchange") return await exchange(req);
    return error(404, "Not found.");
  } catch (e) {
    console.error("auth-steam", route, e instanceof Error ? e.message : e);
    return error(500, "Sign-in failed. Please try again.");
  }
});

async function start(req: Request, params: URLSearchParams): Promise<Response> {
  if (!await allow(`start:${await sha256Base64Url(clientIp(req))}`, 30, 3600)) {
    return text(429, "Too many sign-in attempts. Try again in an hour.");
  }

  const handshake = validateStart(params);
  if (!handshake) return text(400, "Invalid sign-in request. Start again from AimOdometer.");

  await admin.from("auth_pending").delete().lt("expires_at", new Date().toISOString());
  const { data, error: e } = await admin.from("auth_pending").insert(handshake).select("id").single();
  if (e) throw new Error(e.message);
  return redirect(steamLoginUrl(`${BASE}/callback?pending=${data.id}`, SUPABASE_URL));
}

async function callback(params: URLSearchParams): Promise<Response> {
  const pendingId = params.get("pending") ?? "";
  const { data: pending } = await admin.from("auth_pending")
    .select("id, port, state")
    .eq("id", pendingId)
    .is("code_hash", null)
    .gt("expires_at", new Date().toISOString())
    .maybeSingle();
  if (!pending) return text(400, "This sign-in has expired. Start again from AimOdometer.");

  const back = (query: Record<string, string>) =>
    redirect(`http://127.0.0.1:${pending.port}/callback?${new URLSearchParams({ state: pending.state, ...query })}`);

  const assertion = readAssertion(params, `${BASE}/callback?pending=${pending.id}`);
  if (!assertion.ok) {
    await admin.from("auth_pending").delete().eq("id", pending.id);
    return back({ error: assertion.reason });
  }

  if (!await verifyWithSteam(params)) {
    await admin.from("auth_pending").delete().eq("id", pending.id);
    return back({ error: "invalid" });
  }

  const userId = await userFor(assertion.steamId);
  const code = randomToken();
  const { error: e } = await admin.from("auth_pending")
    .update({ code_hash: await sha256Base64Url(code), user_id: userId })
    .eq("id", pending.id);
  if (e) throw new Error(e.message);
  return back({ code });
}

async function exchange(req: Request): Promise<Response> {
  if (!await allow(`exchange:${await sha256Base64Url(clientIp(req))}`, 30, 3600)) {
    return error(429, "Too many attempts. Try again in an hour.");
  }

  const body = await req.json().catch(() => null);
  const code = typeof body?.code === "string" ? body.code : "";
  const verifier = typeof body?.verifier === "string" ? body.verifier : "";
  if (!/^[A-Za-z0-9_-]{43}$/.test(code) || !/^[A-Za-z0-9_-]{43,128}$/.test(verifier)) {
    return error(400, "Invalid code.");
  }

  // Single use: the row is deleted whatever happens next.
  const { data: pending } = await admin.from("auth_pending")
    .delete()
    .eq("code_hash", await sha256Base64Url(code))
    .gt("expires_at", new Date().toISOString())
    .select("challenge, user_id")
    .maybeSingle();
  if (!pending?.user_id || !safeEqual(await sha256Base64Url(verifier), pending.challenge)) {
    return error(400, "This sign-in has expired. Start again.");
  }

  const { data: userData, error: userError } = await admin.auth.admin.getUserById(pending.user_id);
  if (userError || !userData.user.email) throw new Error(userError?.message ?? "user has no email");

  // A one-time login token for that user, verified right away: the result is an ordinary Supabase session.
  const { data: link, error: linkError } = await admin.auth.admin.generateLink({ type: "magiclink", email: userData.user.email });
  if (linkError) throw new Error(linkError.message);
  const { data: verified, error: verifyError } = await anonClient().auth.verifyOtp({
    token_hash: link.properties.hashed_token,
    type: "email",
  });
  if (verifyError || !verified.session) throw new Error(verifyError?.message ?? "no session");

  const session = verified.session;
  const { data: profile } = await admin.from("profiles")
    .select("steam_id, persona_name, avatar_url, profile_url")
    .eq("user_id", pending.user_id)
    .single();
  return json({
    access_token: session.access_token,
    refresh_token: session.refresh_token,
    expires_at: session.expires_at,
    user_id: pending.user_id,
    profile,
  });
}

/** The Supabase user of a SteamID64: found by profile, or created together with the profile. */
async function userFor(steamId: string): Promise<string> {
  const { data: existing } = await admin.from("profiles").select("user_id").eq("steam_id", steamId).maybeSingle();
  const player = await summary(steamId);
  if (existing) {
    if (player) await admin.from("profiles").update(profileFields(player)).eq("user_id", existing.user_id);
    return existing.user_id;
  }

  const { data: created, error: e } = await admin.auth.admin.createUser({
    email: `steam_${steamId}@${SERVICE_EMAIL_DOMAIN}`,
    email_confirm: true,
    app_metadata: { provider: "steam", steam_id: steamId },
  });
  if (e) throw new Error(e.message);

  const userId = created.user.id;
  const { error: profileError } = await admin.from("profiles")
    .insert({ user_id: userId, steam_id: steamId, ...(player ? profileFields(player) : {}) });
  if (profileError) {
    await admin.auth.admin.deleteUser(userId); // never leave a user without a profile
    throw new Error(profileError.message);
  }

  return userId;
}

async function summary(steamId: string) {
  const key = Deno.env.get("STEAM_WEB_API_KEY")?.trim();
  if (!key) {
    console.error("STEAM_WEB_API_KEY is not set");
    return null;
  }

  try {
    return await playerSummary(steamId, key);
  } catch (e) {
    console.error("GetPlayerSummaries failed", e instanceof Error ? e.message : e);
    return null;
  }
}

function profileFields(player: NonNullable<Awaited<ReturnType<typeof summary>>>) {
  return {
    persona_name: player.personaName,
    avatar_url: player.avatarUrl,
    profile_url: player.profileUrl,
    country_code: player.countryCode,
    steam_refreshed_at: new Date().toISOString(),
  };
}

function redirect(location: string): Response {
  return new Response(null, { status: 302, headers: { Location: location, "Cache-Control": "no-store" } });
}

// Plain text: the supabase.co domain does not serve HTML from functions.
function text(status: number, message: string): Response {
  return new Response(message, { status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
}
