-- Daily streaks on leaderboards: days in a row with at least 1 m of movement (StatsSummary.ActiveDayMinimumCm), the
-- same rule as the app's own streak. Days are the players' local dates, so a streak still counts when its last day is
-- up to two days before the server's UTC date (players west of UTC are a day behind; today may not be synced yet).

create function public.player_streak(p_user uuid)
returns integer
language sql
stable
security definer
set search_path = ''
as $$
    with active as (
        select s.day
        from public.daily_stats s
        where s.user_id = p_user and s.day >= current_date - 2000
        group by s.day
        having sum(s.centimeters) >= 100
    ),
    runs as (
        select a.day, a.day - (row_number() over (order by a.day))::integer as run
        from active a
    ),
    latest as (
        select max(r.day) as last_day, count(*)::integer as days
        from runs r
        group by r.run
        order by max(r.day) desc
        limit 1
    )
    select coalesce((select l.days from latest l where l.last_day >= current_date - 2), 0);
$$;

revoke all on function public.player_streak(uuid) from public, anon, authenticated;
grant execute on function public.player_streak(uuid) to service_role;

-- ---------------------------------------------------------------- boards return the streak too

drop function public.world_board(text, text, integer);
create function public.world_board(p_period text, p_game text, p_limit integer)
returns table (rank integer, user_id uuid, persona_name text, avatar_url text, centimeters double precision, players integer,
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[], titles text[],
               streak integer)
language sql
stable
security definer
set search_path = ''
as $$
    select w.rank, w.user_id, p.persona_name, p.avatar_url, w.centimeters, w.players,
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(w.user_id), p.titles, public.player_streak(w.user_id)
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
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[], titles text[],
               streak integer)
language sql
stable
security definer
set search_path = ''
as $$
    with people as (
        select p.user_id, p.persona_name, p.avatar_url, p.titles, p.user_id = p_user as is_me
        from public.profiles p
        where p.user_id = p_user
           or (p.steam_id = any (p_friend_steam_ids) and p.share_with_friends and not p.hidden_from_leaderboards)
    ),
    totals as (
        select pe.user_id, pe.persona_name, pe.avatar_url, pe.titles, coalesce(sum(s.centimeters), 0) as centimeters, pe.is_me
        from people pe
        left join public.daily_stats s
            on s.user_id = pe.user_id
           and s.day >= public.period_start(p_period)
           and (p_game = '*' or s.game_key = p_game)
        group by pe.user_id, pe.persona_name, pe.avatar_url, pe.titles, pe.is_me
    )
    select t.user_id, t.persona_name, t.avatar_url, t.centimeters, t.is_me,
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(t.user_id), t.titles, public.player_streak(t.user_id)
    from totals t
    cross join lateral public.player_dpi(t.user_id, p_period, p_game) d
    order by t.centimeters desc, t.persona_name;
$$;

revoke all on function public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text)
    from public, anon, authenticated;
grant execute on function public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text) to service_role;
