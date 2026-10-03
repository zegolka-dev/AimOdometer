-- Row level security and integrity of the cloud schema. Everything runs in one transaction and is rolled back.
-- Run: npx supabase test db --linked   (or --local with a local stack)
begin;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions;

select plan(30);

-- Two users, A and B, each with a profile, a PC and one day of statistics.
insert into auth.users (id, instance_id, aud, role, email)
values ('aaaaaaaa-0000-4000-8000-000000000001', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'a@rls.test.invalid'),
       ('bbbbbbbb-0000-4000-8000-000000000002', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'b@rls.test.invalid');
insert into public.profiles (user_id, steam_id)
values ('aaaaaaaa-0000-4000-8000-000000000001', '76561197960000001'),
       ('bbbbbbbb-0000-4000-8000-000000000002', '76561197960000002');
insert into public.devices (id, user_id, pc_id)
values ('a0000000-0000-4000-8000-00000000000a', 'aaaaaaaa-0000-4000-8000-000000000001', gen_random_uuid()),
       ('b0000000-0000-4000-8000-00000000000b', 'bbbbbbbb-0000-4000-8000-000000000002', gen_random_uuid());
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
values ('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '2026-10-01', 'steam:730', 100),
       ('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '2026-10-01', '', 50),
       ('bbbbbbbb-0000-4000-8000-000000000002', 'b0000000-0000-4000-8000-00000000000b', '2026-10-01', '', 200);

-- ------------------------------------------------------------ integrity (as the owner role)

select throws_ok(
    $$insert into public.daily_stats (user_id, device_id, day, centimeters)
      values ('aaaaaaaa-0000-4000-8000-000000000001', 'b0000000-0000-4000-8000-00000000000b', '2026-10-02', 1)$$,
    '23503', null, 'a stats row cannot point at another user''s PC');
select throws_ok(
    $$insert into public.daily_stats (user_id, device_id, day, centimeters)
      values ('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '2026-10-02', 20000000)$$,
    '23514', null, 'more than 100 km in one row is rejected by the database');
select throws_ok(
    $$insert into public.daily_stats (user_id, device_id, day, centimeters)
      values ('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '2026-10-02', -1)$$,
    '23514', null, 'negative distance is rejected');
select throws_ok(
    $$insert into public.profiles (user_id, steam_id) values (gen_random_uuid(), 'not-a-steam-id')$$,
    '23514', null, 'steam_id must be a SteamID64');

-- ------------------------------------------------------------ as user A

set local role authenticated;
select set_config('request.jwt.claims', '{"sub":"aaaaaaaa-0000-4000-8000-000000000001","role":"authenticated"}', true);

select results_eq('select steam_id from public.profiles', $$values ('76561197960000001'::text)$$, 'A sees only A''s profile');
select is((select count(*)::int from public.devices), 1, 'A sees only A''s PC');
select is((select count(*)::int from public.daily_stats), 2, 'A sees only A''s statistics');
select is((select sum(centimeters) from public.daily_stats), 150::float8, 'none of B''s distance is visible');
select is((select centimeters from public.my_daily_totals()), 150::float8, 'my_daily_totals sums A''s games');
select is((select count(*)::int from public.my_devices()), 1, 'my_devices lists A''s PC only');
select is((select centimeters from public.my_devices()), 150::float8, 'my_devices totals A''s distance');

select throws_ok(
    $$insert into public.daily_stats (user_id, device_id, day, centimeters)
      values ('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '2026-10-03', 1)$$,
    '42501', null, 'clients cannot write statistics directly (only the sync function can)');
select throws_ok(
    $$update public.daily_stats set centimeters = 1e6$$,
    '42501', null, 'clients cannot change statistics');
select throws_ok(
    $$delete from public.daily_stats$$,
    '42501', null, 'clients cannot delete statistics');
select throws_ok(
    $$insert into public.devices (user_id, pc_id) values ('aaaaaaaa-0000-4000-8000-000000000001', gen_random_uuid())$$,
    '42501', null, 'clients cannot create PCs directly');
select throws_ok(
    $$update public.profiles set steam_id = '76561197960000009'$$,
    '42501', null, 'clients cannot change their SteamID');
select throws_ok(
    $$update public.profiles set hidden_from_leaderboards = false$$,
    '42501', null, 'clients cannot lift a moderation flag');
select lives_ok(
    $$update public.profiles set show_in_world = true, share_with_friends = false$$,
    'clients can change their own privacy switches');
select lives_ok(
    $$update public.profiles set show_in_world = true where user_id = 'bbbbbbbb-0000-4000-8000-000000000002'$$,
    'updating someone else''s profile runs but touches nothing');
select throws_ok($$select * from public.auth_pending$$, '42501', null, 'sign-in handshakes are invisible');
select throws_ok($$select * from public.rate_limits$$, '42501', null, 'rate limits are invisible');
select throws_ok($$select public.hit_rate_limit('x', 1, 60)$$, '42501', null, 'clients cannot call the rate limiter');
select throws_ok(
    $$select public.replace_daily_stats('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a', '[]'::jsonb)$$,
    '42501', null, 'clients cannot call the sync writer');

-- ------------------------------------------------------------ anonymous

reset role;
set local role anon;
select set_config('request.jwt.claims', '{"role":"anon"}', true);
select throws_ok($$select * from public.profiles$$, '42501', null, 'anonymous callers see no profiles');
select throws_ok($$select * from public.my_daily_totals()$$, '42501', null, 'anonymous callers cannot read totals');

-- ------------------------------------------------------------ back to the owner: effects and account deletion

reset role;
select is(
    (select show_in_world from public.profiles where user_id = 'bbbbbbbb-0000-4000-8000-000000000002'), false,
    'B''s profile was not changed by A');
select is(
    (select show_in_world from public.profiles where user_id = 'aaaaaaaa-0000-4000-8000-000000000001'), true,
    'A''s own privacy switch was saved');

-- Re-uploading a day replaces that PC's rows for the day (a merged game must not be counted twice).
select is(
    public.replace_daily_stats('aaaaaaaa-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-00000000000a',
        '[{"day":"2026-10-01","gameKey":"valorant","mouseKey":"","centimeters":70,"clicks":3,"moveSeconds":60,"peakSpeed":100}]'::jsonb),
    1, 'replace_daily_stats inserts the uploaded rows');
select results_eq(
    $$select game_key, centimeters from public.daily_stats where user_id = 'aaaaaaaa-0000-4000-8000-000000000001' and day = '2026-10-01'$$,
    $$values ('valorant'::text, 70::float8)$$,
    'the day''s old rows of that PC are replaced, not added to');

delete from auth.users where id = 'aaaaaaaa-0000-4000-8000-000000000001';
select is(
    (select count(*)::int from public.daily_stats where user_id = 'aaaaaaaa-0000-4000-8000-000000000001')
      + (select count(*)::int from public.devices where user_id = 'aaaaaaaa-0000-4000-8000-000000000001')
      + (select count(*)::int from public.profiles where user_id = 'aaaaaaaa-0000-4000-8000-000000000001'),
    0, 'deleting the account deletes the profile, PCs and statistics');

select * from finish();
rollback;
