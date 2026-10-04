// GET /social/friends?period&game  The caller and their Steam friends who use AimOdometer (and share with friends).
// GET /social/world?period&game    The top of the world leaderboard (opt-in players) and the caller's own place.
// period: week | month | all; game: '*' (all movement) or a game key such as "steam:730".
// Other players' user ids never leave the server: rows carry name, avatar and distance only.
import { admin, allow, callerOf, error, json } from "../_shared/server.ts";
import { friendList } from "../_shared/steam.ts";
import { validateBoard } from "../_shared/validate.ts";

const FRIENDS_TTL_MS = 3600 * 1000;

Deno.serve(async (req) => {
  if (req.method !== "GET") return error(405, "Use GET.");
  try {
    const user = await callerOf(req);
    if (!user) return error(401, "Sign in first.");
    if (!await allow(`social:${user.id}`, 300, 3600)) return error(429, "Too many requests. Try again later.");

    const url = new URL(req.url);
    const board = validateBoard(url.searchParams);
    if (!board) return error(400, "Invalid period or game.");
    const route = url.pathname.split("/").filter(Boolean).pop();
    if (route === "friends") return await friends(user.id, board.period, board.game);
    if (route === "world") return await world(user.id, board.period, board.game);
    return error(404, "Not found.");
  } catch (e) {
    console.error("social", e instanceof Error ? e.message : e);
    return error(500, "Could not load the leaderboard.");
  }
});

async function friends(userId: string, period: string, game: string): Promise<Response> {
  const { steamIds, isPrivate } = await cachedFriends(userId);
  const { data, error: e } = await admin.rpc("friends_board", {
    p_user: userId,
    p_friend_steam_ids: steamIds,
    p_period: period,
    p_game: game,
  });
  if (e) throw new Error(e.message);
  const rows = (data as FriendRow[]).map((r) => ({
    name: r.persona_name,
    avatar: r.avatar_url,
    centimeters: r.centimeters,
    isMe: r.is_me,
    ...fairPlay(r),
  }));
  return json({ private: isPrivate, friendsOnSteam: steamIds.length, rows });
}

async function world(userId: string, period: string, game: string): Promise<Response> {
  const [top, mine, profile] = await Promise.all([
    admin.rpc("world_board", { p_period: period, p_game: game, p_limit: 100 }),
    admin.rpc("world_rank", { p_user: userId, p_period: period, p_game: game }),
    admin.from("profiles").select("show_in_world").eq("user_id", userId).single(),
  ]);
  if (top.error || mine.error) throw new Error((top.error ?? mine.error)!.message);
  const board = top.data as WorldRow[];
  const rows = board.map((r) => ({
    rank: r.rank,
    name: r.persona_name,
    avatar: r.avatar_url,
    centimeters: r.centimeters,
    isMe: r.user_id === userId,
    ...fairPlay(r),
  }));
  const me = (mine.data as { rank: number; centimeters: number; players: number }[])[0] ?? null;
  return json({
    participating: profile.data?.show_in_world === true,
    players: board[0]?.players ?? 0,
    rows,
    me,
  });
}

/** The caller's Steam friends, from the cache or from Steam (at most once an hour). */
async function cachedFriends(userId: string): Promise<{ steamIds: string[]; isPrivate: boolean }> {
  const { data: cached } = await admin.from("friend_cache")
    .select("steam_ids, is_private, fetched_at")
    .eq("user_id", userId)
    .maybeSingle();
  if (cached && Date.now() - Date.parse(cached.fetched_at) < FRIENDS_TTL_MS) {
    return { steamIds: cached.steam_ids, isPrivate: cached.is_private };
  }

  const fallback = { steamIds: cached?.steam_ids ?? [], isPrivate: cached?.is_private ?? false };
  const key = Deno.env.get("STEAM_WEB_API_KEY")?.trim();
  const { data: profile } = await admin.from("profiles").select("steam_id").eq("user_id", userId).single();
  if (!key || !profile) return fallback;

  try {
    const list = await friendList(profile.steam_id, key);
    const result = list.private ? { steamIds: [], isPrivate: true } : { steamIds: list.steamIds, isPrivate: false };
    await admin.from("friend_cache").upsert({
      user_id: userId,
      steam_ids: result.steamIds,
      is_private: result.isPrivate,
      fetched_at: new Date().toISOString(),
    });
    return result;
  } catch (e) {
    console.error("GetFriendList failed", e instanceof Error ? e.message.replace(/key=[^&\s]*/g, "key=***") : e);
    return fallback;
  }
}

// What the distance and the fastest flick were measured with, and badges for inflated distance (FairPlay.cs).
function fairPlay(r: FairPlayColumns) {
  return {
    dpi: r.dpi === null ? null : Math.round(r.dpi),
    peakSpeed: r.peak_speed,
    peakDpi: r.peak_dpi === null ? null : Math.round(r.peak_dpi),
    badges: r.badges ?? [],
  };
}

interface FairPlayColumns {
  dpi: number | null;
  peak_speed: number;
  peak_dpi: number | null;
  badges: string[] | null;
}

interface FriendRow extends FairPlayColumns {
  persona_name: string;
  avatar_url: string;
  centimeters: number;
  is_me: boolean;
}

interface WorldRow extends FairPlayColumns {
  rank: number;
  user_id: string;
  persona_name: string;
  avatar_url: string;
  centimeters: number;
  players: number;
}
