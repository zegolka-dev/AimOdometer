// POST /download-click {asset, language, place, referrer?} from the website when a download button is clicked.
// Sent with navigator.sendBeacon as text/plain, so there is no CORS preflight and nothing to read back.
// The IP is never stored: only a hash of it salted with the day and a server secret, to count people, not clicks.
import { admin, allow, clientIp, error } from "../_shared/server.ts";
import { sha256Base64Url } from "../_shared/crypto.ts";
import { validateDownloadClick } from "../_shared/validate.ts";

const SALT = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const done = () => new Response(null, { status: 204, headers: { "Access-Control-Allow-Origin": "*" } });

Deno.serve(async (req) => {
  if (req.method !== "POST") return error(405, "Use POST.");
  try {
    const ip = clientIp(req);
    // Someone clicking over and over is still counted, just not more than 20 times an hour.
    if (!await allow(`download:${await sha256Base64Url(ip)}`, 20, 3600)) return done();

    const click = validateDownloadClick(await req.json().catch(() => null));
    if (!click) return error(400, "Bad click.");

    const day = new Date().toISOString().slice(0, 10);
    const visitor = (await sha256Base64Url(`${day}|${ip}|${SALT}`)).slice(0, 16);
    const { error: e } = await admin.from("site_downloads").insert({ ...click, visitor });
    if (e) throw new Error(e.message);
    return done();
  } catch (e) {
    console.error("download-click", e instanceof Error ? e.message : e);
    return error(500, "Not counted.");
  }
});
