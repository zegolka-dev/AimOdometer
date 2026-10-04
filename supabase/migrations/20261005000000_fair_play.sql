-- Fair play: DPI on the leaderboards and "shame" badges for inflated distance.
--
-- Distance is counts / DPI, so telling AimOdometer a DPI far below the mouse's real one multiplies every kilometre.
-- Leaderboards now show the DPI each distance and fastest flick were measured with, and badges for players who
-- crossed a limit even once. The limits match src/AimOdometer.Core/Fun/FairPlay.cs:
--   clown      a day of at least 10 km at a distance-weighted DPI below 200
--   blockhead  at least 100 m moved with a DPI below 100
--   fool       a flick of at least 30 m/s
--   booster    a day or a flick the sync rejected as impossible (over 100 km a day or 50 m/s)

alter table public.daily_stats
    add column dpi      double precision not null default 0 check (dpi >= 0 and dpi <= 100000),     -- 0 = not sent (before 0.1.0-beta.14)
    add column peak_dpi double precision not null default 0 check (peak_dpi >= 0 and peak_dpi <= 100000);

-- Set by the sync function when it rejects an impossible day or flick; never cleared. Owners can update only the two
-- privacy columns (column-level grant), so this one stays server-only.
alter table public.profiles add column boosted_at timestamptz;

-- ---------------------------------------------------------------- upload

create or replace function public.replace_daily_stats(p_user uuid, p_device uuid, p_rows jsonb)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_count integer;
begin
    delete from public.daily_stats
    where user_id = p_user
      and device_id = p_device
      and day in (select distinct (r ->> 'day')::date from jsonb_array_elements(p_rows) r);

    insert into public.daily_stats (user_id, device_id, day, game_key, mouse_key, centimeters, clicks, move_seconds, peak_speed,
                                    dpi, peak_dpi)
    select p_user, p_device, (r ->> 'day')::date, r ->> 'gameKey', r ->> 'mouseKey',
           (r ->> 'centimeters')::double precision, (r ->> 'clicks')::bigint, (r ->> 'moveSeconds')::integer,
           (r ->> 'peakSpeed')::double precision,
           coalesce((r ->> 'dpi')::double precision, 0), coalesce((r ->> 'peakDpi')::double precision, 0)
    from jsonb_array_elements(p_rows) r;

    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- ---------------------------------------------------------------- badges

create function public.fair_play_badges(p_user uuid)
returns text[]
language sql
stable
security definer
set search_path = ''
as $$
    select array_remove(array[
        case when exists (
            select 1
            from (
                select sum(s.centimeters) as cm,
                       sum(s.centimeters * s.dpi) filter (where s.dpi > 0) / nullif(sum(s.centimeters) filter (where s.dpi > 0), 0) as dpi
                from public.daily_stats s
                where s.user_id = p_user
                group by s.day
            ) d
            where d.cm >= 1000000 and d.dpi < 200) then 'clown' end,
        case when (select coalesce(sum(s.centimeters), 0) from public.daily_stats s
                   where s.user_id = p_user and s.dpi > 0 and s.dpi < 100) >= 10000 then 'blockhead' end,
        case when exists (select 1 from public.daily_stats s where s.user_id = p_user and s.peak_speed >= 3000) then 'fool' end,
        case when (select p.boosted_at from public.profiles p where p.user_id = p_user) is not null then 'booster' end
    ], null);
$$;

-- What a player's distance and fastest flick in a period were measured with.
create function public.player_dpi(p_user uuid, p_period text, p_game text)
returns table (dpi double precision, peak_speed double precision, peak_dpi double precision)
language sql
stable
security definer
set search_path = ''
as $$
    with rows as (
        select s.centimeters, s.dpi, s.peak_speed, s.peak_dpi
        from public.daily_stats s
        where s.user_id = p_user
          and s.day >= public.period_start(p_period)
          and (p_game = '*' or s.game_key = p_game)
    )
    select
        (select sum(r.centimeters * r.dpi) / nullif(sum(r.centimeters), 0) from rows r where r.dpi > 0),
        coalesce((select max(r.peak_speed) from rows r), 0),
        (select r.peak_dpi from rows r where r.peak_dpi > 0 order by r.peak_speed desc limit 1);
$$;

-- ---------------------------------------------------------------- boards with DPI and badges

drop function public.world_board(text, text, integer);
create function public.world_board(p_period text, p_game text, p_limit integer)
returns table (rank integer, user_id uuid, persona_name text, avatar_url text, centimeters double precision, players integer,
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[])
language sql
stable
security definer
set search_path = ''
as $$
    select w.rank, w.user_id, p.persona_name, p.avatar_url, w.centimeters, w.players,
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(w.user_id)
    from (
        select * from public.world_ranks
        where period = p_period and game_key = p_game
        order by rank
        limit least(greatest(p_limit, 1), 100)
    ) w
    join public.profiles p on p.user_id = w.user_id
    cross join lateral public.player_dpi(w.user_id, p_period, p_game) d
    order by w.rank, p.persona_name;
$$;

drop function public.friends_board(uuid, text[], text, text);
create function public.friends_board(p_user uuid, p_friend_steam_ids text[], p_period text, p_game text)
returns table (user_id uuid, persona_name text, avatar_url text, centimeters double precision, is_me boolean,
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[])
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
    ),
    totals as (
        select pe.user_id, pe.persona_name, pe.avatar_url, coalesce(sum(s.centimeters), 0) as centimeters, pe.is_me
        from people pe
        left join public.daily_stats s
            on s.user_id = pe.user_id
           and s.day >= public.period_start(p_period)
           and (p_game = '*' or s.game_key = p_game)
        group by pe.user_id, pe.persona_name, pe.avatar_url, pe.is_me
    )
    select t.user_id, t.persona_name, t.avatar_url, t.centimeters, t.is_me,
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(t.user_id)
    from totals t
    cross join lateral public.player_dpi(t.user_id, p_period, p_game) d
    order by t.centimeters desc, t.persona_name;
$$;

revoke all on function public.fair_play_badges(uuid), public.player_dpi(uuid, text, text),
    public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text) from public, anon, authenticated;
grant execute on function public.fair_play_badges(uuid), public.player_dpi(uuid, text, text),
    public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text) to service_role;
