-- DoubleMark: user print templates and scan history (per account)

create table if not exists public.user_print_templates (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users(id) on delete cascade,
    name text not null,
    description text,
    width_mm numeric not null,
    height_mm numeric not null,
    printer_name text,
    template_data jsonb not null default '{}'::jsonb,
    is_default boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists public.user_scan_history (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users(id) on delete cascade,
    raw_code text not null,
    code_hash text not null,
    source text,
    gs_count integer default 0,
    has_ai01 boolean default false,
    has_ai21 boolean default false,
    has_ai91 boolean default false,
    has_ai92 boolean default false,
    gtin text,
    serial text,
    scanned_at timestamptz not null default now(),
    created_at timestamptz not null default now()
);

create index if not exists user_print_templates_user_id_idx
    on public.user_print_templates (user_id);

create index if not exists user_scan_history_user_id_scanned_at_idx
    on public.user_scan_history (user_id, scanned_at desc);

create index if not exists user_scan_history_user_id_code_hash_idx
    on public.user_scan_history (user_id, code_hash);

alter table public.user_print_templates enable row level security;
alter table public.user_scan_history enable row level security;

-- user_print_templates policies
create policy "user_print_templates_select_own"
    on public.user_print_templates for select
    using (auth.uid() = user_id);

create policy "user_print_templates_insert_own"
    on public.user_print_templates for insert
    with check (auth.uid() = user_id);

create policy "user_print_templates_update_own"
    on public.user_print_templates for update
    using (auth.uid() = user_id)
    with check (auth.uid() = user_id);

create policy "user_print_templates_delete_own"
    on public.user_print_templates for delete
    using (auth.uid() = user_id);

-- user_scan_history policies
create policy "user_scan_history_select_own"
    on public.user_scan_history for select
    using (auth.uid() = user_id);

create policy "user_scan_history_insert_own"
    on public.user_scan_history for insert
    with check (auth.uid() = user_id);

create policy "user_scan_history_delete_own"
    on public.user_scan_history for delete
    using (auth.uid() = user_id);

-- Trim scan history to last 100 entries per user (delete oldest first)
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
            ) - 100,
            0
        )
    );
    return new;
end;
$$;

drop trigger if exists trim_user_scan_history_trigger on public.user_scan_history;
create trigger trim_user_scan_history_trigger
    after insert on public.user_scan_history
    for each row execute function public.trim_user_scan_history();
