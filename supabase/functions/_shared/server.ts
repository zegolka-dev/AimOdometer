// Shared server plumbing: the service-role client, the caller's identity, JSON responses, rate limits.
import { createClient, type SupabaseClient, type User } from "npm:@supabase/supabase-js@2.117.2";

export const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
export const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;

/** Service-role client: bypasses row level security, so every query must filter by the verified user itself. */
export const admin: SupabaseClient = createClient(SUPABASE_URL, SERVICE_ROLE_KEY, {
  auth: { persistSession: false, autoRefreshToken: false },
});

/** A fresh anonymous client (for verifying the one-time login token). */
export function anonClient(): SupabaseClient {
  return createClient(SUPABASE_URL, ANON_KEY, { auth: { persistSession: false, autoRefreshToken: false } });
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
  });
}

export function error(status: number, message: string): Response {
  return json({ error: message }, status);
}

/** The signed-in user behind the Bearer token, checked with Supabase Auth (works with any JWT signing key). */
export async function callerOf(req: Request): Promise<User | null> {
  const header = req.headers.get("Authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice(7) : "";
  if (!token) return null;
  const { data, error: e } = await admin.auth.getUser(token);
  return e ? null : data.user;
}

/** True while `key` is within `max` hits per window. Fails closed if the database cannot be asked. */
export async function allow(key: string, max: number, windowSeconds: number): Promise<boolean> {
  const { data, error: e } = await admin.rpc("hit_rate_limit", { p_key: key, p_max: max, p_window_seconds: windowSeconds });
  if (e) {
    console.error("rate limit check failed", e.message);
    return false;
  }
  return data === true;
}

/** Client IP as seen by the Supabase gateway (for limits only, never stored with user data). */
export function clientIp(req: Request): string {
  return (req.headers.get("x-forwarded-for") ?? "").split(",")[0].trim() || "unknown";
}
