-- Friends and world leaderboards: who is visible to whom. One transaction, rolled back.
begin;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions;

select plan(16);

-- A (me), B (friend, shares), C (friend, does not share with friends), D (stranger, in the world board),
-- E (in the world board but hidden by moderation).
insert into auth.users (id, instance_id, aud, role, email)
select ('00000000-0000-4000-8000-00000000000' || n)::uuid, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', n || '@social.test.invalid'
from unnest(array['1', '2', '3', '4', '5']) n;

insert into public.profiles (user_id, steam_id, persona_name, share_with_friends, show_in_world, hidden_from_leaderboards)
values ('00000000-0000-4000-8000-000000000001', '76561190000000001', 'A', true, true, false),
       ('00000000-0000-4000-8000-000000000002', '76561190000000002', 'B', true, false, false),
       ('00000000-0000-4000-8000-000000000003', '76561190000000003', 'C', false, true, false),
       ('00000000-0000-4000-8000-000000000004', '76561190000000004', 'D', true, true, false),
       ('00000000-0000-4000-8000-000000000005', '76561190000000005', 'E', true, true, true);

insert into public.devices (id, user_id, pc_id)
select ('10000000-0000-4000-8000-00000000000' || n)::uuid, ('00000000-0000-4000-8000-00000000000' || n)::uuid, gen_random_uuid()
from unnest(array['1', '2', '3', '4', '5']) n;

-- Distances today: A 300 (200 of it in CS2), B 500, C 400, D 900, E 9000; and A 1000 a year ago.
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
values ('00000000-0000-4000-8000-000000000001', '10000000-0000-4000-8000-000000000001', current_date, 'steam:730', 200),
       ('00000000-0000-4000-8000-000000000001', '10000000-0000-4000-8000-000000000001', current_date, '', 100),
       ('00000000-0000-4000-8000-000000000001', '10000000-0000-4000-8000-000000000001', current_date - 400, '', 1000),
       ('00000000-0000-4000-8000-000000000002', '10000000-0000-4000-8000-000000000002', current_date, '', 500),
       ('00000000-0000-4000-8000-000000000003', '10000000-0000-4000-8000-000000000003', current_date, 'steam:730', 400),
       ('00000000-0000-4000-8000-000000000004', '10000000-0000-4000-8000-000000000004', current_date, 'steam:730', 900),
       ('00000000-0000-4000-8000-000000000005', '10000000-0000-4000-8000-000000000005', current_date, '', 9000);

refresh materialized view public.world_ranks;

-- ------------------------------------------------------------ friends

select results_eq(
    $$select persona_name, centimeters, is_me from public.friends_board('00000000-0000-4000-8000-000000000001',
        array['76561190000000002', '76561190000000003', '76561190000000005'], 'week', '*')$$,
    $$values ('B'::text, 500::float8, false), ('A'::text, 300::float8, true)$$,
    'friends board: me and friends who share; not C (does not share), not E (hidden)');
select results_eq(
    $$select persona_name from public.friends_board('00000000-0000-4000-8000-000000000001', array['76561190000000004'], 'week', '*')
      where not is_me$$,
    $$values ('D'::text)$$,
    'a Steam friend who shares is listed even when not in the world board');
select is(
    (select count(*)::int from public.friends_board('00000000-0000-4000-8000-000000000001', array[]::text[], 'week', '*')),
    1, 'without friends only I am listed');
select is(
    (select centimeters from public.friends_board('00000000-0000-4000-8000-000000000001', array[]::text[], 'all', '*')),
    1300::float8, 'all time includes last year');
select is(
    (select centimeters from public.friends_board('00000000-0000-4000-8000-000000000001', array[]::text[], 'week', 'steam:730')),
    200::float8, 'a game board counts only that game');
select is(
    (select centimeters from public.friends_board('00000000-0000-4000-8000-000000000002', array['76561190000000001'], 'week', 'steam:730')
     where is_me), 0::float8, 'a friend with nothing in that game is still listed with 0');

-- ------------------------------------------------------------ world

select results_eq(
    $$select rank, persona_name from public.world_board('week', '*', 100)$$,
    $$values (1, 'D'::text), (2, 'C'::text), (3, 'A'::text)$$,
    'world board: only opted-in players (not B), hidden E excluded');
select results_eq(
    $$select rank, persona_name from public.world_board('week', 'steam:730', 100)$$,
    $$values (1, 'D'::text), (2, 'C'::text), (3, 'A'::text)$$,
    'per-game world board');
select is((select count(*)::int from public.world_board('week', '', 100)), 0, 'there is no board for "not a game"');
select results_eq(
    $$select rank, centimeters, players from public.world_rank('00000000-0000-4000-8000-000000000001', 'week', '*')$$,
    $$values (3, 300::float8, 3)$$,
    'my place, distance and the number of players');
select is((select count(*)::int from public.world_rank('00000000-0000-4000-8000-000000000002', 'week', '*')), 0,
    'not opted in: no world place');
select is((select count(*)::int from public.world_board('week', '*', 1)), 1, 'the limit applies');
select results_eq(
    $$select rank from public.world_rank('00000000-0000-4000-8000-000000000001', 'all', '*')$$,
    $$values (1)$$,
    'all time: A (1300 with last year) passes D (900)');

-- ------------------------------------------------------------ clients cannot read any of it

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"00000000-0000-4000-8000-000000000001","role":"authenticated"}', true);
select throws_ok($$select * from public.world_ranks$$, '42501', null, 'clients cannot read the ranks directly');
select throws_ok($$select * from public.friends_board('00000000-0000-4000-8000-000000000001', array[]::text[], 'week', '*')$$,
    '42501', null, 'clients cannot call the friends board directly');
select throws_ok($$select * from public.friend_cache$$, '42501', null, 'friend lists are invisible');

select * from finish();
rollback;
