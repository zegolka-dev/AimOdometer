// GET /profile — the caller's profile, refreshed from Steam (name, avatar) at most every six hours.
import { admin, allow, callerOf, error, json } from "../_shared/server.ts";
import { playerSummary } from "../_shared/steam.ts";

const REFRESH_HOURS = 6;

Deno.serve(async (req) => {
  if (req.method !== "GET") return error(405, "Use GET.");
  try {
    const user = await callerOf(req);
    if (!user) return error(401, "Sign in first.");
    if (!await allow(`profile:${user.id}`, 120, 3600)) return error(429, "Too many requests. Try again later.");

    const columns = "steam_id, persona_name, avatar_url, profile_url, country_code, share_with_friends, show_in_world, steam_refreshed_at";
    const { data: profile, error: e } = await admin.from("profiles").select(columns).eq("user_id", user.id).single();
    if (e) return error(404, "No profile.");

    const age = profile.steam_refreshed_at ? Date.now() - Date.parse(profile.steam_refreshed_at) : Infinity;
    const key = Deno.env.get("STEAM_WEB_API_KEY")?.trim();
    // Why name and avatar may be missing, for the window's log (never contains the key).
    let steamStatus = age > REFRESH_HOURS * 3600 * 1000 ? (key ? "pending" : "no_key") : "fresh";
    if (key && age > REFRESH_HOURS * 3600 * 1000) {
      try {
        const player = await playerSummary(profile.steam_id, key);
        steamStatus = player ? "ok" : "not_found";
        if (player) {
          const fields = {
            persona_name: player.personaName,
            avatar_url: player.avatarUrl,
            profile_url: player.profileUrl,
            country_code: player.countryCode,
            steam_refreshed_at: new Date().toISOString(),
          };
          await admin.from("profiles").update(fields).eq("user_id", user.id);
          Object.assign(profile, fields);
        }
      } catch (steamError) {
        steamStatus = steamError instanceof Error ? steamError.message.replace(/key=[^&\s]*/g, "key=***").slice(0, 120) : "error";
        console.error("profile refresh", steamStatus);
      }
    }

    return json({ ...profile, steam_status: steamStatus });
  } catch (e) {
    console.error("profile", e instanceof Error ? e.message : e);
    return error(500, "Could not load the profile.");
  }
});
