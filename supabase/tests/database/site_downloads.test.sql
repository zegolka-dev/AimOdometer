-- The website's download counter is private: visitors and app users can neither read nor write it.
-- Run: npx supabase test db --linked   (or --local with a local stack)
begin;
create extension if not exists pgtap with schema extensions;
set local search_path = public, extensions;

select plan(5);

insert into public.site_downloads (asset, language, place, referrer, visitor)
values ('setup', 'ru', 'hero', 'www.youtube.com', 'abcdefghijklmnop');

select throws_ok(
    $$insert into public.site_downloads (asset, language, place, visitor) values ('setup', 'de', 'hero', 'abcdefghijklmnop')$$,
    '23514', null, 'only known languages are stored');
select throws_ok(
    $$insert into public.site_downloads (asset, language, place, visitor) values ('setup', 'en', 'hero', '1.2.3.4')$$,
    '23514', null, 'the visitor column holds a 16-character hash, not an address');

set local role anon;
select throws_ok($$select count(*) from public.site_downloads$$, '42501', null, 'anonymous visitors cannot read the counter');
select throws_ok(
    $$insert into public.site_downloads (asset, language, place, visitor) values ('setup', 'en', 'hero', 'abcdefghijklmnop')$$,
    '42501', null, 'anonymous visitors cannot write to it directly');

set local role authenticated;
select throws_ok($$select count(*) from public.site_downloads$$, '42501', null, 'signed-in app users cannot read it either');

select * from finish();
rollback;
