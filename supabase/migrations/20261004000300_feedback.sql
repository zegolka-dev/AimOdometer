-- Complaints and suggestions from the app ("Feedback" button). Written only by the feedback Edge Function, read by
-- the author in the Supabase dashboard (Table Editor › feedback) or with tools/Read-Feedback.ps1.
create table public.feedback (
    id            bigint      generated always as identity primary key,
    created_at    timestamptz not null default now(),
    kind          text        not null check (kind in ('bug', 'idea', 'other')),
    message       text        not null check (char_length(message) between 3 and 4000),
    contact       text        check (char_length(contact) <= 200),
    app_version   text        check (char_length(app_version) <= 40),
    os            text        check (char_length(os) <= 120),
    language      text        check (char_length(language) <= 10),
    steam_id      text,                                   -- when the sender was signed in
    persona_name  text,
    status        text        not null default 'new' check (status in ('new', 'read', 'done'))
);

create index feedback_new on public.feedback (created_at desc) where status = 'new';

alter table public.feedback enable row level security;  -- no policies: invisible to clients
grant all on public.feedback to service_role;
