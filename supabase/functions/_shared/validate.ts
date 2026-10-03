// Plausibility checks for uploaded statistics (anti-cheat, first line). The database has check constraints as well.

/** Limits per uploaded row and per day. Generous for real play, impossible for a mouse on a desk. */
export const LIMITS = {
  maxRows: 5000,
  maxCentimetersPerDay: 10_000_000, // 100 km a day across games and mice of one PC
  maxClicksPerDay: 2_000_000,
  maxMoveSecondsPerDay: 86_400,
  maxPeakSpeed: 5_000, // cm/s; the fastest human flicks are ~10 m/s
  firstDay: "2020-01-01",
} as const;

export interface SyncRow {
  day: string;
  gameKey: string;
  mouseKey: string;
  centimeters: number;
  clicks: number;
  moveSeconds: number;
  peakSpeed: number;
}

export interface SyncRequest {
  pcId: string;
  pcName: string;
  rows: SyncRow[];
}

export type Validation =
  | { ok: true; request: SyncRequest; rejected: number }
  | { ok: false; error: string };

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DAY = /^\d{4}-\d{2}-\d{2}$/;
const GAME_KEY = /^[A-Za-z0-9:._ -]{0,64}$/;
const MOUSE_KEY = /^[0-9a-f]{0,32}$/;

/**
 * Validates a sync upload. Malformed requests fail as a whole; implausible rows are dropped (and counted) so one bad
 * day can never block the rest of a PC's history. `now` is injectable for tests.
 */
export function validateSync(body: unknown, now = new Date()): Validation {
  if (typeof body !== "object" || body === null) return { ok: false, error: "Body must be a JSON object." };
  const b = body as Record<string, unknown>;
  if (typeof b.pcId !== "string" || !UUID.test(b.pcId)) return { ok: false, error: "pcId must be a UUID." };
  const pcName = typeof b.pcName === "string" ? b.pcName.slice(0, 64) : "";
  if (!Array.isArray(b.rows)) return { ok: false, error: "rows must be an array." };
  if (b.rows.length > LIMITS.maxRows) return { ok: false, error: `At most ${LIMITS.maxRows} rows per request.` };

  // Latest acceptable day: tomorrow in UTC covers every time zone (UTC+14) without allowing the future.
  const latest = new Date(now.getTime() + 24 * 3600 * 1000).toISOString().slice(0, 10);
  const rows: SyncRow[] = [];
  const seen = new Set<string>();
  let rejected = 0;
  for (const raw of b.rows) {
    const row = readRow(raw);
    const key = row && `${row.day}|${row.gameKey}|${row.mouseKey}`;
    if (!row || row.day < LIMITS.firstDay || row.day > latest || seen.has(key!)) {
      rejected++;
      continue;
    }

    seen.add(key!);
    rows.push(row);
  }

  // Per-day totals of this PC: if a day is impossible as a whole, all its rows go.
  const perDay = new Map<string, { cm: number; clicks: number; seconds: number }>();
  for (const row of rows) {
    const t = perDay.get(row.day) ?? { cm: 0, clicks: 0, seconds: 0 };
    t.cm += row.centimeters;
    t.clicks += row.clicks;
    t.seconds += row.moveSeconds;
    perDay.set(row.day, t);
  }

  const impossible = new Set(
    [...perDay].filter(([, t]) =>
      t.cm > LIMITS.maxCentimetersPerDay || t.clicks > LIMITS.maxClicksPerDay || t.seconds > LIMITS.maxMoveSecondsPerDay
    ).map(([day]) => day),
  );
  const accepted = rows.filter((r) => !impossible.has(r.day));
  rejected += rows.length - accepted.length;
  return { ok: true, request: { pcId: b.pcId.toLowerCase(), pcName, rows: accepted }, rejected };
}

function readRow(raw: unknown): SyncRow | null {
  if (typeof raw !== "object" || raw === null) return null;
  const r = raw as Record<string, unknown>;
  const day = r.day;
  const gameKey = r.gameKey ?? "";
  const mouseKey = r.mouseKey ?? "";
  if (typeof day !== "string" || !DAY.test(day) || !isRealDate(day)) return null;
  if (typeof gameKey !== "string" || !GAME_KEY.test(gameKey)) return null;
  if (typeof mouseKey !== "string" || !MOUSE_KEY.test(mouseKey)) return null;
  const centimeters = number(r.centimeters, 0, LIMITS.maxCentimetersPerDay);
  const clicks = integer(r.clicks ?? 0, 0, LIMITS.maxClicksPerDay);
  const moveSeconds = integer(r.moveSeconds ?? 0, 0, LIMITS.maxMoveSecondsPerDay);
  const peakSpeed = number(r.peakSpeed ?? 0, 0, LIMITS.maxPeakSpeed);
  if (centimeters === null || clicks === null || moveSeconds === null || peakSpeed === null) return null;
  return { day, gameKey, mouseKey, centimeters, clicks, moveSeconds, peakSpeed };
}

function isRealDate(day: string): boolean {
  const date = new Date(`${day}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === day;
}

function number(value: unknown, min: number, max: number): number | null {
  return typeof value === "number" && Number.isFinite(value) && value >= min && value <= max ? value : null;
}

function integer(value: unknown, min: number, max: number): number | null {
  const n = number(value, min, max);
  return n !== null && Number.isInteger(n) ? n : null;
}

/** Sign-in /start parameters. */
export function validateStart(params: URLSearchParams): { challenge: string; port: number; state: string } | null {
  const challenge = params.get("challenge") ?? "";
  const state = params.get("state") ?? "";
  const port = Number(params.get("port"));
  if (!/^[A-Za-z0-9_-]{43}$/.test(challenge)) return null;
  if (!/^[A-Za-z0-9_-]{16,128}$/.test(state)) return null;
  if (!Number.isInteger(port) || port < 1024 || port > 65535) return null;
  return { challenge, port, state };
}
