-- Phase 8: friends and leaderboards.
--
-- Visibility rules (author's decisions of 2026-10-01):
--   * friends see you by default (profiles.share_with_friends = true), Steam friendship being mutual;
--   * the world leaderboard is opt-in (profiles.show_in_world = false by default);
--   * moderation can hide anyone from both (profiles.hidden_from_leaderboards).
-- Clients never read other users' rows: the boards are served by the "social" Edge Function through the
-- service-role-only functions below, which return names, avatars and distances, never user ids.

create extension if not exists pg_cron;

-- ---------------------------------------------------------------- friend lists

-- Steam friend lists, cached for an hour (the Steam Web API is rate limited; lists rarely change).
create table public.friend_cache (
    user_id     uuid        primary key references auth.users (id) on delete cascade,
    steam_ids   text[]      not null default '{}',
    is_private  boolean     not null default false,  -- the friend list is not public on Steam
    fetched_at  timestamptz not null default now()
);

alter table public.friend_cache enable row level security;  -- no policies: invisible to clients
grant all on public.friend_cache to service_role;

-- ---------------------------------------------------------------- periods

-- First day of a leaderboard period: this ISO week, this calendar month, or everything.
create function public.period_start(p_period text)
returns date
language sql
stable
set search_path = ''
as $$
    select case p_period
        when 'week' then date_trunc('week', current_date)::date
        when 'month' then date_trunc('month', current_date)::date
        else date '2020-01-01'
    end;
$$;

-- ---------------------------------------------------------------- world leaderboard

-- Ranks of everyone who opted in, per period and per game ('*' = all movement). Precomputed because ranking on every
-- request does not scale on the free tier; the unique index gives "my place" in one lookup even outside the top 100.
create materialized view public.world_ranks as
with periods (period) as (values ('week'), ('month'), ('all')),
players as (
    select user_id from public.profiles where show_in_world and not hidden_from_leaderboards
),
totals as (
    select p.period, '*'::text as game_key, s.user_id, sum(s.centimeters) as centimeters
    from periods p
    join public.daily_stats s on s.day >= public.period_start(p.period)
    join players pl on pl.user_id = s.user_id
    group by p.period, s.user_id
    union all
    select p.period, s.game_key, s.user_id, sum(s.centimeters)
    from periods p
    join public.daily_stats s on s.day >= public.period_start(p.period) and s.game_key <> ''
    join players pl on pl.user_id = s.user_id
    group by p.period, s.game_key, s.user_id
)
select period, game_key, user_id, centimeters,
       rank() over (partition by period, game_key order by centimeters desc)::integer as rank,
       count(*) over (partition by period, game_key)::integer as players
from totals
where centimeters > 0;

create unique index world_ranks_key on public.world_ranks (period, game_key, user_id);
create index world_ranks_rank on public.world_ranks (period, game_key, rank);

revoke all on public.world_ranks from public, anon, authenticated;
grant select on public.world_ranks to service_role;

select cron.schedule('refresh-world-ranks', '*/15 * * * *', 'refresh materialized view concurrently public.world_ranks');

-- Top of the world leaderboard.
create function public.world_board(p_period text, p_game text, p_limit integer)
returns table (rank integer, user_id uuid, persona_name text, avatar_url text, centimeters double precision, players integer)
language sql
stable
security definer
set search_path = ''
as $$
    select w.rank, w.user_id, p.persona_name, p.avatar_url, w.centimeters, w.players
    from public.world_ranks w
    join public.profiles p on p.user_id = w.user_id
    where w.period = p_period and w.game_key = p_game
    order by w.rank, p.persona_name
    limit least(greatest(p_limit, 1), 100);
$$;

-- One player's place (null when not ranked: not opted in, hidden, or no distance in the period).
create function public.world_rank(p_user uuid, p_period text, p_game text)
returns table (rank integer, centimeters double precision, players integer)
language sql
stable
security definer
set search_path = ''
as $$
    select w.rank, w.centimeters, w.players
    from public.world_ranks w
    where w.user_id = p_user and w.period = p_period and w.game_key = p_game;
$$;

-- ---------------------------------------------------------------- friends leaderboard

-- The caller and those of their Steam friends who use AimOdometer and share with friends, with live totals.
create function public.friends_board(p_user uuid, p_friend_steam_ids text[], p_period text, p_game text)
returns table (user_id uuid, persona_name text, avatar_url text, centimeters double precision, is_me boolean)
language sql
stable
security definer
set search_path = ''
as $$
    with people as (
        select p.user_id, p.persona_name, p.avatar_url, p.user_id = p_user as is_me
        from public.profiles p
        where p.user_id = p_user
           or (p.steam_id = any (p_friend_steam_ids) and p.share_with_friends and not p.hidden_from_leaderboards)
    )
    select pe.user_id, pe.persona_name, pe.avatar_url, coalesce(sum(s.centimeters), 0) as centimeters, pe.is_me
    from people pe
    left join public.daily_stats s
        on s.user_id = pe.user_id
       and s.day >= public.period_start(p_period)
       and (p_game = '*' or s.game_key = p_game)
    group by pe.user_id, pe.persona_name, pe.avatar_url, pe.is_me
    order by 4 desc, 2;
$$;

revoke all on function public.period_start(text) from public, anon, authenticated;
revoke all on function public.world_board(text, text, integer), public.world_rank(uuid, text, text),
    public.friends_board(uuid, text[], text, text) from public, anon, authenticated;
grant execute on function public.period_start(text), public.world_board(text, text, integer),
    public.world_rank(uuid, text, text), public.friends_board(uuid, text[], text, text) to service_role;
