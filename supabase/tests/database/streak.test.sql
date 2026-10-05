-- Streaks on leaderboards (player_streak): days in a row with at least 1 m, ending today or shortly before.
-- Run: npx supabase test db --linked   (or --local with a local stack)
begin;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions;

select plan(5);

insert into auth.users (id, instance_id, aud, role, email)
values ('aaaaaaaa-0000-4000-8000-0000000000a1', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'a@streak.test.invalid'),
       ('bbbbbbbb-0000-4000-8000-0000000000b2', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'b@streak.test.invalid'),
       ('cccccccc-0000-4000-8000-0000000000c3', '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', 'c@streak.test.invalid');
insert into public.devices (id, user_id, pc_id)
values ('a0000000-0000-4000-8000-0000000000a1', 'aaaaaaaa-0000-4000-8000-0000000000a1', gen_random_uuid()),
       ('b0000000-0000-4000-8000-0000000000b2', 'bbbbbbbb-0000-4000-8000-0000000000b2', gen_random_uuid()),
       ('c0000000-0000-4000-8000-0000000000c3', 'cccccccc-0000-4000-8000-0000000000c3', gen_random_uuid());

-- A: the last 30 days up to today, an older run of 5 days before a gap, and one day with only 50 cm (does not count).
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
select 'aaaaaaaa-0000-4000-8000-0000000000a1', 'a0000000-0000-4000-8000-0000000000a1', current_date - g, '', 500
from generate_series(0, 29) g;
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
select 'aaaaaaaa-0000-4000-8000-0000000000a1', 'a0000000-0000-4000-8000-0000000000a1', current_date - g, '', 500
from generate_series(32, 36) g;
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
values ('aaaaaaaa-0000-4000-8000-0000000000a1', 'a0000000-0000-4000-8000-0000000000a1', current_date - 30, '', 50);
-- B: 3 days ending yesterday, split across two games of 60 cm each (120 cm a day counts).
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
select 'bbbbbbbb-0000-4000-8000-0000000000b2', 'b0000000-0000-4000-8000-0000000000b2', current_date - g, k, 60
from generate_series(1, 3) g cross join (values ('steam:730'), ('')) v(k);
-- C: 10 days that ended five days ago: the streak is over.
insert into public.daily_stats (user_id, device_id, day, game_key, centimeters)
select 'cccccccc-0000-4000-8000-0000000000c3', 'c0000000-0000-4000-8000-0000000000c3', current_date - g, '', 900
from generate_series(5, 14) g;

select is(public.player_streak('aaaaaaaa-0000-4000-8000-0000000000a1'), 30, 'the current run counts, not an older one; 50 cm days break it');
select is(public.player_streak('bbbbbbbb-0000-4000-8000-0000000000b2'), 3, 'a run ending yesterday is alive; games add up per day');
select is(public.player_streak('cccccccc-0000-4000-8000-0000000000c3'), 0, 'a run that ended days ago is over');
select is(public.player_streak(gen_random_uuid()), 0, 'no data, no streak');

set local role anon;
select throws_ok($$select public.player_streak(gen_random_uuid())$$, '42501', null, 'clients cannot call it directly');

select * from finish();
rollback;
