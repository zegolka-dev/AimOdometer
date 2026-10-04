// POST /feedback {kind, message, contact?, version?, os?, language?} — a complaint or a suggestion from the app.
// Works without signing in; when the caller is signed in, their Steam name is attached so the author can answer.
import { admin, allow, callerOf, clientIp, error, json } from "../_shared/server.ts";
import { sha256Base64Url } from "../_shared/crypto.ts";
import { validateFeedback } from "../_shared/validate.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return error(405, "Use POST.");
  try {
    if (!await allow(`feedback:${await sha256Base64Url(clientIp(req))}`, 10, 3600)) {
      return error(429, "Too many messages. Try again in an hour.");
    }

    const feedback = validateFeedback(await req.json().catch(() => null));
    if (!feedback) return error(400, "Write at least a few words (up to 4000 characters).");

    const user = req.headers.has("Authorization") ? await callerOf(req) : null;
    let steam: { steam_id: string; persona_name: string } | null = null;
    if (user) {
      const { data } = await admin.from("profiles").select("steam_id, persona_name").eq("user_id", user.id).maybeSingle();
      steam = data;
    }

    const { error: e } = await admin.from("feedback").insert({
      kind: feedback.kind,
      message: feedback.message,
      contact: feedback.contact,
      app_version: feedback.version,
      os: feedback.os,
      language: feedback.language,
      steam_id: steam?.steam_id ?? null,
      persona_name: steam?.persona_name ?? null,
    });
    if (e) throw new Error(e.message);
    return json({ sent: true });
  } catch (e) {
    console.error("feedback", e instanceof Error ? e.message : e);
    return error(500, "Could not send. Please try again later.");
  }
});
