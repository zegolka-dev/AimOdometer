-- Uploading a day replaces all of that PC's rows for the day, in one transaction. Plain upserts would keep rows whose
-- key changed on the PC (a game merged or renamed, a mouse excluded) and count that day twice.
create function public.replace_daily_stats(p_user uuid, p_device uuid, p_rows jsonb)
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

    insert into public.daily_stats (user_id, device_id, day, game_key, mouse_key, centimeters, clicks, move_seconds, peak_speed)
    select p_user, p_device, (r ->> 'day')::date, r ->> 'gameKey', r ->> 'mouseKey',
           (r ->> 'centimeters')::double precision, (r ->> 'clicks')::bigint, (r ->> 'moveSeconds')::integer,
           (r ->> 'peakSpeed')::double precision
    from jsonb_array_elements(p_rows) r;

    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

revoke all on function public.replace_daily_stats(uuid, uuid, jsonb) from public, anon, authenticated;
grant execute on function public.replace_daily_stats(uuid, uuid, jsonb) to service_role;
