// POST /sync — the window uploads daily totals of this PC (date x game x mouse). Each uploaded day replaces that PC's
// rows for the day (replace_daily_stats, one transaction), so retries, catch-up after being offline and changed game
// rules on the PC are all safe. The client never splits one day across two requests.
import { admin, allow, callerOf, error, json } from "../_shared/server.ts";
import { validateSync } from "../_shared/validate.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return error(405, "Use POST.");
  try {
    const user = await callerOf(req);
    if (!user) return error(401, "Sign in first.");
    if (!await allow(`sync:${user.id}`, 120, 3600)) return error(429, "Too many syncs. Try again later.");

    const validation = validateSync(await req.json().catch(() => null));
    if (!validation.ok) return error(400, validation.error);
    const { request, rejected } = validation;

    const { data: device, error: deviceError } = await admin.from("devices")
      .upsert({ user_id: user.id, pc_id: request.pcId, name: request.pcName, last_sync_at: new Date().toISOString() },
        { onConflict: "user_id,pc_id" })
      .select("id")
      .single();
    if (deviceError) throw new Error(deviceError.message);

    if (request.rows.length > 0) {
      const { error: e } = await admin.rpc("replace_daily_stats", { p_user: user.id, p_device: device.id, p_rows: request.rows });
      if (e) throw new Error(e.message);
    }

    if (rejected > 0) console.warn(`sync: ${rejected} implausible rows rejected for ${user.id}`);
    return json({ accepted: request.rows.length, rejected, deviceId: device.id });
  } catch (e) {
    console.error("sync", e instanceof Error ? e.message : e);
    return error(500, "Sync failed. It will be retried.");
  }
});
