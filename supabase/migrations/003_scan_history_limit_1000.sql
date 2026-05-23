-- Raise per-user cloud scan history cap from 100 to 1000

create or replace function public.trim_user_scan_history()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
begin
    delete from public.user_scan_history
    where id in (
        select id
        from public.user_scan_history
        where user_id = new.user_id
        order by scanned_at asc, created_at asc
        limit greatest(
            (
                select count(*)
                from public.user_scan_history
                where user_id = new.user_id
            ) - 1000,
            0
        )
    );
    return new;
end;
$$;
