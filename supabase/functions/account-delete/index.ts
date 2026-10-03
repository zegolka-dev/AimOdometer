// POST /account-delete — deletes the caller's account. Every table references auth.users with ON DELETE CASCADE,
// so the profile, PCs and statistics go with it. Local statistics on the PC are not touched.
import { admin, allow, callerOf, error, json } from "../_shared/server.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return error(405, "Use POST.");
  try {
    const user = await callerOf(req);
    if (!user) return error(401, "Sign in first.");
    if (!await allow(`delete:${user.id}`, 5, 3600)) return error(429, "Too many attempts. Try again later.");

    const { error: e } = await admin.auth.admin.deleteUser(user.id);
    if (e) throw new Error(e.message);
    return json({ deleted: true });
  } catch (e) {
    console.error("account-delete", e instanceof Error ? e.message : e);
    return error(500, "Could not delete the account. Please try again.");
  }
});
