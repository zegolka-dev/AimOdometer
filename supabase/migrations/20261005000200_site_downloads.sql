-- Clicks on the website's download buttons. Written only by the download-click Edge Function, read by the author in
-- the Supabase dashboard (Table Editor › site_downloads) or with tools/Read-Downloads.ps1. No IP addresses are kept:
-- `visitor` is a hash of the IP that changes every day, so one person's clicks on one day count once and cannot be
-- followed across days.
create table public.site_downloads (
    id          bigint      generated always as identity primary key,
    created_at  timestamptz not null default now(),
    asset       text        not null check (asset in ('setup', 'portable')),
    language    text        not null check (language in ('en', 'ru')),
    place       text        not null check (place in ('hero', 'middle', 'final')),
    referrer    text        check (char_length(referrer) <= 100),   -- host of the site the visitor came from
    visitor     text        not null check (char_length(visitor) = 16)
);

create index site_downloads_created on public.site_downloads (created_at desc);

alter table public.site_downloads enable row level security;  -- no policies: invisible to clients
revoke all on public.site_downloads from anon, authenticated;
grant all on public.site_downloads to service_role;
