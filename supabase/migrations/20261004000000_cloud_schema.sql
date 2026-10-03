-- AimOdometer cloud schema (phase 7): Steam profiles, PCs, daily totals, sign-in handshakes, rate limits.
--
-- Rules:
--   * Row level security on every table. Clients (role "authenticated") may only read their own rows.
--   * Clients never write statistics directly: the "sync" Edge Function validates and upserts them with the
--     service role. The only direct client write is the privacy switches on their own profile.
--   * Every user-owned row references auth.users with ON DELETE CASCADE, so deleting the account deletes everything.

-- Nothing in "public" is exposed unless granted below (the project also has "expose new tables" switched off).
revoke all on all tables in schema public from anon, authenticated;
revoke all on all functions in schema public from anon, authenticated, public;
alter default privileges in schema public revoke all on tables from anon, authenticated;
alter default privileges in schema public revoke all on functions from anon, authenticated, public;

-- ---------------------------------------------------------------- profiles

create table public.profiles (
    user_id                   uuid        primary key references auth.users (id) on delete cascade,
    steam_id                  text        not null unique check (steam_id ~ '^[0-9]{17}$'),
    persona_name              text        not null default '' check (char_length(persona_name) <= 64),
    avatar_url                text        not null default '' check (char_length(avatar_url) <= 512),
    profile_url               text        not null default '' check (char_length(profile_url) <= 512),
    country_code              text        check (country_code ~ '^[A-Z]{2}$'),
    -- Privacy (decisions of 2026-10-01): friends see you by default, the world leaderboard is opt-in.
    share_with_friends        boolean     not null default true,
    show_in_world             boolean     not null default false,
    -- Set by moderation (anti-cheat), never by the user.
    hidden_from_leaderboards  boolean     not null default false,
    steam_refreshed_at        timestamptz,
    created_at                timestamptz not null default now(),
    updated_at                timestamptz not null default now()
);

alter table public.profiles enable row level security;

create policy "profiles: read own" on public.profiles
    for select to authenticated using (user_id = (select auth.uid()));

create policy "profiles: update own privacy" on public.profiles
    for update to authenticated using (user_id = (select auth.uid())) with check (user_id = (select auth.uid()));

grant select on public.profiles to authenticated;
-- Column-level: only the two privacy switches are writable by the owner.
grant update (share_with_friends, show_in_world) on public.profiles to authenticated;

-- ---------------------------------------------------------------- devices (PCs)

-- One row per PC the user syncs from. "pc_id" is a random id generated on the PC; nothing identifies the hardware.
create table public.devices (
    id            uuid        primary key default gen_random_uuid(),
    user_id       uuid        not null references auth.users (id) on delete cascade,
    pc_id         uuid        not null,
    name          text        not null default '' check (char_length(name) <= 64),
    created_at    timestamptz not null default now(),
    last_sync_at  timestamptz not null default now(),
    unique (user_id, pc_id),
    unique (id, user_id)  -- target of the composite key below: a stats row can only point at its owner's PC
);

alter table public.devices enable row level security;

create policy "devices: read own" on public.devices
    for select to authenticated using (user_id = (select auth.uid()));

grant select on public.devices to authenticated;

-- ---------------------------------------------------------------- daily_stats

-- Daily totals per PC x game x mouse. The values are absolute totals of that day (not increments), so syncing the
-- same day again simply replaces the row: uploads are idempotent and an offline PC catches up on its next sync.
create table public.daily_stats (
    user_id        uuid             not null references auth.users (id) on delete cascade,
    device_id      uuid             not null,
    day            date             not null check (day >= date '2020-01-01'),
    game_key       text             not null default '' check (char_length(game_key) <= 64),   -- '' = not a game
    mouse_key      text             not null default '' check (char_length(mouse_key) <= 32),  -- hashed on the PC
    centimeters    double precision not null check (centimeters >= 0 and centimeters <= 10000000),  -- <= 100 km
    clicks         bigint           not null default 0 check (clicks >= 0 and clicks <= 2000000),
    move_seconds   integer          not null default 0 check (move_seconds >= 0 and move_seconds <= 86400),
    peak_speed     double precision not null default 0 check (peak_speed >= 0 and peak_speed <= 5000), -- cm/s
    updated_at     timestamptz      not null default now(),
    primary key (user_id, device_id, day, game_key, mouse_key),
    foreign key (device_id, user_id) references public.devices (id, user_id) on delete cascade
);

create index daily_stats_user_day on public.daily_stats (user_id, day);

alter table public.daily_stats enable row level security;

create policy "daily_stats: read own" on public.daily_stats
    for select to authenticated using (user_id = (select auth.uid()));

grant select on public.daily_stats to authenticated;

-- ---------------------------------------------------------------- auth_pending (service role only)

-- Steam sign-in handshakes: created by /start, completed by /callback, consumed by /exchange. Five minutes to live.
create table public.auth_pending (
    id          uuid        primary key default gen_random_uuid(),
    challenge   text        not null check (challenge ~ '^[A-Za-z0-9_-]{43}$'),  -- base64url(SHA-256(verifier))
    port        integer     not null check (port between 1024 and 65535),
    state       text        not null check (char_length(state) between 16 and 128),
    code_hash   text,                                                          -- set by /callback
    user_id     uuid        references auth.users (id) on delete cascade,
    created_at  timestamptz not null default now(),
    expires_at  timestamptz not null default now() + interval '5 minutes'
);

alter table public.auth_pending enable row level security;  -- no policies: invisible to clients

-- ---------------------------------------------------------------- rate_limits (service role only)

create table public.rate_limits (
    key           text        not null,
    window_start  timestamptz not null,
    hits          integer     not null default 0,
    primary key (key, window_start)
);

alter table public.rate_limits enable row level security;  -- no policies: invisible to clients

-- Counts one hit for "key" in the current fixed window; true while the limit is not exceeded.
create function public.hit_rate_limit(p_key text, p_max integer, p_window_seconds integer)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_window timestamptz := to_timestamp(floor(extract(epoch from now()) / p_window_seconds) * p_window_seconds);
    v_hits integer;
begin
    insert into public.rate_limits (key, window_start, hits) values (p_key, v_window, 1)
    on conflict (key, window_start) do update set hits = public.rate_limits.hits + 1
    returning hits into v_hits;
    -- Old windows are dropped now and then; the table stays tiny.
    if random() < 0.01 then
        delete from public.rate_limits where window_start < now() - interval '2 days';
    end if;
    return v_hits <= p_max;
end;
$$;

revoke all on function public.hit_rate_limit(text, integer, integer) from public, anon, authenticated;
grant execute on function public.hit_rate_limit(text, integer, integer) to service_role;

-- ---------------------------------------------------------------- service role

grant all on public.profiles, public.devices, public.daily_stats, public.auth_pending, public.rate_limits to service_role;

-- updated_at bookkeeping
create function public.touch_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    new.updated_at := now();
    return new;
end;
$$;

revoke all on function public.touch_updated_at() from public, anon, authenticated;

create trigger profiles_touch before update on public.profiles
    for each row execute function public.touch_updated_at();
create trigger daily_stats_touch before update on public.daily_stats
    for each row execute function public.touch_updated_at();

-- ---------------------------------------------------------------- read helpers for the window

-- The caller's totals per day across all PCs, games and mice (row level security applies: invoker rights).
create function public.my_daily_totals(p_from date default date '2020-01-01')
returns table (day date, centimeters double precision, clicks bigint, move_seconds bigint)
language sql
stable
security invoker
set search_path = ''
as $$
    select s.day, sum(s.centimeters), sum(s.clicks)::bigint, sum(s.move_seconds)::bigint
    from public.daily_stats s
    where s.user_id = (select auth.uid()) and s.day >= p_from
    group by s.day
    order by s.day;
$$;

-- The caller's PCs with their all-time distance.
create function public.my_devices()
returns table (id uuid, pc_id uuid, name text, last_sync_at timestamptz, centimeters double precision)
language sql
stable
security invoker
set search_path = ''
as $$
    select d.id, d.pc_id, d.name, d.last_sync_at, coalesce(sum(s.centimeters), 0)
    from public.devices d
    left join public.daily_stats s on s.device_id = d.id and s.user_id = d.user_id
    where d.user_id = (select auth.uid())
    group by d.id
    order by d.last_sync_at desc;
$$;

revoke all on function public.my_daily_totals(date), public.my_devices() from public, anon;
grant execute on function public.my_daily_totals(date), public.my_devices() to authenticated;
