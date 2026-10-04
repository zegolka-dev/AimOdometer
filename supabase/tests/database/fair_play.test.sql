-- Fair play: DPI on the boards and badges for inflated distance (src/AimOdometer.Core/Fun/FairPlay.cs). Rolled back.
begin;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions;

select plan(12);

-- F: clown (10 km in a day at 100 DPI). G: honest (800 and 1600 DPI). H: fool (35 m/s flick) and blockhead (200 m at
-- 50 DPI). I: booster (caught by the sync).
insert into auth.users (id, instance_id, aud, role, email)
select ('00000000-0000-4000-8000-0000000000f' || n)::uuid, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', n || '@fair.test.invalid'
from unnest(array['1', '2', '3', '4']) n;

insert into public.profiles (user_id, steam_id, persona_name, share_with_friends, show_in_world, boosted_at)
values ('00000000-0000-4000-8000-0000000000f1', '76561190000000101', 'F', true, true, null),
       ('00000000-0000-4000-8000-0000000000f2', '76561190000000102', 'G', true, true, null),
       ('00000000-0000-4000-8000-0000000000f3', '76561190000000103', 'H', true, true, null),
       ('00000000-0000-4000-8000-0000000000f4', '76561190000000104', 'I', true, true, now());

insert into public.devices (id, user_id, pc_id)
select ('10000000-0000-4000-8000-0000000000f' || n)::uuid, ('00000000-0000-4000-8000-0000000000f' || n)::uuid, gen_random_uuid()
from unnest(array['1', '2', '3', '4']) n;

insert into public.daily_stats (user_id, device_id, day, game_key, centimeters, peak_speed, dpi, peak_dpi)
values ('00000000-0000-4000-8000-0000000000f1', '10000000-0000-4000-8000-0000000000f1', current_date, '', 1200000, 900, 100, 100),
       ('00000000-0000-4000-8000-0000000000f2', '10000000-0000-4000-8000-0000000000f2', current_date, 'steam:730', 1000, 400, 800, 800),
       ('00000000-0000-4000-8000-0000000000f2', '10000000-0000-4000-8000-0000000000f2', current_date, '', 3000, 600, 1600, 1600),
       ('00000000-0000-4000-8000-0000000000f3', '10000000-0000-4000-8000-0000000000f3', current_date, '', 20000, 3500, 50, 50),
       ('00000000-0000-4000-8000-0000000000f4', '10000000-0000-4000-8000-0000000000f4', current_date, '', 500, 300, 800, 800);

select is(public.fair_play_badges('00000000-0000-4000-8000-0000000000f1'), array['clown'], 'a 12 km day at 100 DPI: clown');
select is(public.fair_play_badges('00000000-0000-4000-8000-0000000000f2'), array[]::text[], 'honest play: no badges');
select is(public.fair_play_badges('00000000-0000-4000-8000-0000000000f3'), array['blockhead', 'fool'], '200 m at 50 DPI and a 35 m/s flick');
select is(public.fair_play_badges('00000000-0000-4000-8000-0000000000f4'), array['booster'], 'caught by the sync: booster');

select results_eq(
    $$select dpi, peak_speed, peak_dpi from public.player_dpi('00000000-0000-4000-8000-0000000000f2', 'week', '*')$$,
    $$values (1400::float8, 600::float8, 1600::float8)$$,
    'distance-weighted DPI (1000 cm at 800, 3000 cm at 1600) and the DPI of the fastest flick');
select results_eq(
    $$select dpi, peak_dpi from public.player_dpi('00000000-0000-4000-8000-0000000000f2', 'week', 'steam:730')$$,
    $$values (800::float8, 800::float8)$$,
    'per game');

select results_eq(
    $$select persona_name, badges, dpi from public.friends_board('00000000-0000-4000-8000-0000000000f2',
        array['76561190000000101'], 'week', '*') order by persona_name$$,
    $$values ('F'::text, array['clown'], 100::float8), ('G'::text, array[]::text[], 1400::float8)$$,
    'friends board carries DPI and badges');

refresh materialized view public.world_ranks;
select ok(
    (select badges = array['clown'] and dpi = 100 from public.world_board('week', '*', 100) where persona_name = 'F'),
    'world board carries DPI and badges');

-- Uploads keep DPI; clients from before 0.1.0-beta.14 send none (0 = unknown).
select public.replace_daily_stats('00000000-0000-4000-8000-0000000000f4', '10000000-0000-4000-8000-0000000000f4',
    '[{"day":"2026-01-01","gameKey":"","mouseKey":"","centimeters":10,"clicks":0,"moveSeconds":0,"peakSpeed":1,"dpi":1600,"peakDpi":3200},
      {"day":"2026-01-02","gameKey":"","mouseKey":"","centimeters":10,"clicks":0,"moveSeconds":0,"peakSpeed":1}]'::jsonb);
select results_eq(
    $$select dpi, peak_dpi from public.daily_stats where user_id = '00000000-0000-4000-8000-0000000000f4' and day < date '2026-02-01' order by day$$,
    $$values (1600::float8, 3200::float8), (0::float8, 0::float8)$$,
    'replace_daily_stats stores DPI, 0 when absent');

-- ------------------------------------------------------------ clients

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"00000000-0000-4000-8000-0000000000f4","role":"authenticated"}', true);
select throws_ok($$select public.fair_play_badges('00000000-0000-4000-8000-0000000000f1')$$, '42501', null,
    'clients cannot call the badge function');
select throws_ok($$update public.profiles set boosted_at = null where user_id = '00000000-0000-4000-8000-0000000000f4'$$,
    '42501', null, 'a booster cannot clear the mark');
select lives_ok($$update public.profiles set show_in_world = false where user_id = '00000000-0000-4000-8000-0000000000f4'$$,
    'privacy switches still work');

select * from finish();
rollback;
