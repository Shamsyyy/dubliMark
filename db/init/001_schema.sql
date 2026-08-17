-- DoubleMark local schema (PostgreSQL)
-- Applied automatically on first `docker compose up` via /docker-entrypoint-initdb.d

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    revoked_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS refresh_tokens_user_id_idx ON refresh_tokens (user_id);

CREATE TABLE IF NOT EXISTS profiles (
    id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    email TEXT,
    company_name TEXT,
    inn TEXT,
    phone TEXT,
    role TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS subscriptions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    plan_id TEXT,
    status TEXT NOT NULL DEFAULT 'trialing',
    current_period_start TIMESTAMPTZ,
    current_period_end TIMESTAMPTZ,
    trial_ends_at TIMESTAMPTZ,
    devices_limit INTEGER NOT NULL DEFAULT 3,
    provider_subscription_id TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS subscriptions_user_id_uidx ON subscriptions (user_id);

CREATE TABLE IF NOT EXISTS payments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    plan_id TEXT,
    amount NUMERIC(12, 2),
    currency TEXT DEFAULT 'RUB',
    status TEXT
);

CREATE INDEX IF NOT EXISTS payments_user_id_idx ON payments (user_id, created_at DESC);

CREATE TABLE IF NOT EXISTS user_devices (
    device_id TEXT NOT NULL,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_name TEXT NOT NULL DEFAULT '',
    platform TEXT NOT NULL DEFAULT 'Windows',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, device_id)
);

CREATE TABLE IF NOT EXISTS user_print_templates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    width_mm NUMERIC NOT NULL,
    height_mm NUMERIC NOT NULL,
    printer_name TEXT,
    template_data JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_default BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS user_print_templates_user_id_idx
    ON user_print_templates (user_id);

CREATE TABLE IF NOT EXISTS user_scan_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    raw_code TEXT NOT NULL,
    code_hash TEXT NOT NULL,
    source TEXT,
    gs_count INTEGER DEFAULT 0,
    has_ai01 BOOLEAN DEFAULT false,
    has_ai21 BOOLEAN DEFAULT false,
    has_ai91 BOOLEAN DEFAULT false,
    has_ai92 BOOLEAN DEFAULT false,
    gtin TEXT,
    serial TEXT,
    scanned_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS user_scan_history_user_id_scanned_at_idx
    ON user_scan_history (user_id, scanned_at DESC);

CREATE INDEX IF NOT EXISTS user_scan_history_user_id_code_hash_idx
    ON user_scan_history (user_id, code_hash);

-- Keep last 1000 scans per user
CREATE OR REPLACE FUNCTION trim_user_scan_history()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    DELETE FROM user_scan_history
    WHERE id IN (
        SELECT id
        FROM user_scan_history
        WHERE user_id = NEW.user_id
        ORDER BY scanned_at ASC, created_at ASC
        LIMIT GREATEST(
            (SELECT COUNT(*) FROM user_scan_history WHERE user_id = NEW.user_id) - 1000,
            0
        )
    );
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_trim_user_scan_history ON user_scan_history;
CREATE TRIGGER trg_trim_user_scan_history
    AFTER INSERT ON user_scan_history
    FOR EACH ROW
    EXECUTE PROCEDURE trim_user_scan_history();
