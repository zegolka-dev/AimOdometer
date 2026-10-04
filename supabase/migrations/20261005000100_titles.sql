-- Honorary titles next to the name on leaderboards: "creator" (the author) and "beta-tester" (everyone who signed in
-- during the beta). Set by the author only: owners can update just the two privacy columns (column-level grant).
-- Shame badges (fair_play_badges) are independent: a title never hides them.

alter table public.profiles
    add column titles text[] not null default '{}' check (titles <@ array['creator', 'beta-tester']::text[]);

-- Everyone signed in so far is a beta tester; the first profile (2026-10-03) is the author's.
update public.profiles set titles = array['beta-tester'];
update public.profiles set titles = array['creator', 'beta-tester']
where user_id = (select user_id from public.profiles order by created_at limit 1);

-- ---------------------------------------------------------------- boards return the titles too

drop function public.world_board(text, text, integer);
create function public.world_board(p_period text, p_game text, p_limit integer)
returns table (rank integer, user_id uuid, persona_name text, avatar_url text, centimeters double precision, players integer,
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[], titles text[])
language sql
stable
security definer
set search_path = ''
as $$
    select w.rank, w.user_id, p.persona_name, p.avatar_url, w.centimeters, w.players,
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(w.user_id), p.titles
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
               dpi double precision, peak_speed double precision, peak_dpi double precision, badges text[], titles text[])
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
           d.dpi, d.peak_speed, d.peak_dpi, public.fair_play_badges(t.user_id), t.titles
    from totals t
    cross join lateral public.player_dpi(t.user_id, p_period, p_game) d
    order by t.centimeters desc, t.persona_name;
$$;

revoke all on function public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text)
    from public, anon, authenticated;
grant execute on function public.world_board(text, text, integer), public.friends_board(uuid, text[], text, text) to service_role;
