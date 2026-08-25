-- DoubleMark VPS Postgres (not Supabase)
-- Fresh install: docker-entrypoint runs db/init/*.sql
-- Existing volume: ./db/apply-migrations.sh  (or psql -f this file)

CREATE TABLE IF NOT EXISTS organizations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    legal_name TEXT NOT NULL,
    inn TEXT,
    phone TEXT,
    email TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT organizations_inn_format CHECK (
        inn IS NULL OR inn ~ '^[0-9]{10}$' OR inn ~ '^[0-9]{12}$'
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS organizations_inn_unique
    ON organizations (inn)
    WHERE inn IS NOT NULL AND inn <> '';

ALTER TABLE profiles
    ADD COLUMN IF NOT EXISTS org_id UUID REFERENCES organizations(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS org_role TEXT NOT NULL DEFAULT 'owner';

ALTER TABLE profiles DROP CONSTRAINT IF EXISTS profiles_org_role_check;
ALTER TABLE profiles ADD CONSTRAINT profiles_org_role_check
    CHECK (org_role IN ('owner', 'admin', 'operator'));

ALTER TABLE subscriptions
    ADD COLUMN IF NOT EXISTS org_id UUID REFERENCES organizations(id) ON DELETE CASCADE;

ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS org_id UUID REFERENCES organizations(id) ON DELETE CASCADE;

ALTER TABLE user_devices
    ADD COLUMN IF NOT EXISTS org_id UUID REFERENCES organizations(id) ON DELETE CASCADE,
    ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ;

ALTER TABLE user_print_templates
    ADD COLUMN IF NOT EXISTS org_id UUID REFERENCES organizations(id) ON DELETE CASCADE;

DO $$
DECLARE
    rec RECORD;
    new_org UUID;
BEGIN
    FOR rec IN
        SELECT id, email, company_name, inn, phone
        FROM profiles
        WHERE org_id IS NULL
    LOOP
        new_org := NULL;
        IF rec.inn IS NOT NULL AND rec.inn <> '' THEN
            SELECT id INTO new_org FROM organizations WHERE inn = rec.inn LIMIT 1;
        END IF;

        IF new_org IS NULL THEN
            INSERT INTO organizations (legal_name, inn, phone, email)
            VALUES (
                COALESCE(NULLIF(rec.company_name, ''), rec.email, 'Клиент'),
                NULLIF(rec.inn, ''),
                rec.phone,
                rec.email
            )
            RETURNING id INTO new_org;
        END IF;

        UPDATE profiles
        SET
            org_id = new_org,
            org_role = CASE
                WHEN EXISTS (
                    SELECT 1 FROM profiles p2
                    WHERE p2.org_id = new_org AND p2.id <> rec.id
                ) THEN 'operator'
                ELSE 'owner'
            END
        WHERE id = rec.id;
    END LOOP;
END
$$;

UPDATE subscriptions s
SET org_id = p.org_id
FROM profiles p
WHERE s.user_id = p.id AND s.org_id IS NULL;

UPDATE payments pay
SET org_id = p.org_id
FROM profiles p
WHERE pay.user_id = p.id AND pay.org_id IS NULL;

UPDATE user_devices d
SET org_id = p.org_id
FROM profiles p
WHERE d.user_id = p.id AND d.org_id IS NULL;

UPDATE user_print_templates t
SET org_id = p.org_id
FROM profiles p
WHERE t.user_id = p.id AND t.org_id IS NULL;

CREATE TABLE IF NOT EXISTS plans (
    id TEXT PRIMARY KEY,
    tier TEXT NOT NULL,
    period TEXT NOT NULL,
    name TEXT NOT NULL,
    price_rub INTEGER NOT NULL,
    devices_limit INTEGER NOT NULL DEFAULT 1,
    trial_days INTEGER NOT NULL DEFAULT 14,
    features JSONB NOT NULL DEFAULT '[]'::jsonb,
    CONSTRAINT plans_tier_check CHECK (tier IN ('base', 'standard', 'elite')),
    CONSTRAINT plans_period_check CHECK (period IN ('monthly', 'yearly'))
);

INSERT INTO plans (id, tier, period, name, price_rub, devices_limit, trial_days, features)
VALUES
    ('base-monthly', 'base', 'monthly', 'Base', 3000, 1, 14,
     '["1 устройство","Неограниченное количество кодов"]'::jsonb),
    ('standard-monthly', 'standard', 'monthly', 'Standard', 5000, 3, 14,
     '["До 3 устройств","Приоритетная поддержка"]'::jsonb),
    ('elite-monthly', 'elite', 'monthly', 'Elite', 10000, 10, 14,
     '["До 10 устройств","Beta-функции"]'::jsonb),
    ('base-yearly', 'base', 'yearly', 'Base', 28800, 1, 30,
     '["1 устройство","Неограниченное количество кодов"]'::jsonb),
    ('standard-yearly', 'standard', 'yearly', 'Standard', 48000, 3, 30,
     '["До 3 устройств","Приоритетная поддержка"]'::jsonb),
    ('elite-yearly', 'elite', 'yearly', 'Elite', 96000, 10, 30,
     '["До 10 устройств","Beta-функции"]'::jsonb)
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS entitlements (
    org_id UUID PRIMARY KEY REFERENCES organizations(id) ON DELETE CASCADE,
    can_download BOOLEAN NOT NULL DEFAULT false,
    devices_limit INTEGER NOT NULL DEFAULT 1,
    features JSONB NOT NULL DEFAULT '{}'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO entitlements (org_id, can_download, devices_limit)
SELECT o.id, false, 1
FROM organizations o
ON CONFLICT (org_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS api_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id UUID REFERENCES users(id) ON DELETE CASCADE,
    device_id TEXT,
    name TEXT,
    token_hash TEXT NOT NULL,
    last_used_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ,
    revoked_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS api_tokens_token_hash_idx ON api_tokens (token_hash);

CREATE TABLE IF NOT EXISTS printers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    device_id TEXT,
    name TEXT NOT NULL,
    model TEXT,
    status TEXT NOT NULL DEFAULT 'offline',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT printers_status_check CHECK (status IN ('ready', 'offline', 'error'))
);

CREATE TABLE IF NOT EXISTS marking_codes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    gtin TEXT,
    serial TEXT,
    payload_hash TEXT NOT NULL,
    payload_enc BYTEA,
    crypto_tail_hash TEXT,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS marking_codes_org_payload_hash_idx
    ON marking_codes (org_id, payload_hash);

CREATE UNIQUE INDEX IF NOT EXISTS marking_codes_org_gtin_serial_idx
    ON marking_codes (org_id, gtin, serial)
    WHERE gtin IS NOT NULL AND serial IS NOT NULL;

CREATE INDEX IF NOT EXISTS marking_codes_org_last_seen_idx
    ON marking_codes (org_id, last_seen_at DESC);

CREATE TABLE IF NOT EXISTS code_operations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id UUID NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    code_id UUID REFERENCES marking_codes(id) ON DELETE SET NULL,
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    device_id TEXT,
    printer_id UUID REFERENCES printers(id) ON DELETE SET NULL,
    kind TEXT NOT NULL,
    duration_ms INTEGER,
    error_code TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT code_operations_kind_check CHECK (kind IN ('scan', 'parse', 'print', 'fail'))
);

CREATE INDEX IF NOT EXISTS code_operations_org_created_idx
    ON code_operations (org_id, created_at DESC);

CREATE OR REPLACE FUNCTION record_code_operation(
    p_org_id UUID,
    p_payload_hash TEXT,
    p_gtin TEXT,
    p_serial TEXT,
    p_crypto_tail_hash TEXT,
    p_payload_enc BYTEA,
    p_kind TEXT,
    p_user_id UUID,
    p_device_id TEXT,
    p_printer_id UUID,
    p_duration_ms INTEGER,
    p_error_code TEXT
)
RETURNS UUID
LANGUAGE plpgsql
AS $$
DECLARE
    v_code UUID;
    v_op UUID;
BEGIN
    IF p_org_id IS NULL THEN
        RAISE EXCEPTION 'org_id required';
    END IF;
    IF p_payload_hash IS NULL OR length(p_payload_hash) < 32 THEN
        RAISE EXCEPTION 'payload_hash required';
    END IF;
    IF p_kind NOT IN ('scan', 'parse', 'print', 'fail') THEN
        RAISE EXCEPTION 'unsupported kind';
    END IF;

    INSERT INTO marking_codes (
        org_id, gtin, serial, payload_hash, payload_enc, crypto_tail_hash
    )
    VALUES (
        p_org_id, p_gtin, p_serial, p_payload_hash, p_payload_enc, p_crypto_tail_hash
    )
    ON CONFLICT (org_id, payload_hash) DO UPDATE
    SET
        gtin = COALESCE(EXCLUDED.gtin, marking_codes.gtin),
        serial = COALESCE(EXCLUDED.serial, marking_codes.serial),
        crypto_tail_hash = COALESCE(EXCLUDED.crypto_tail_hash, marking_codes.crypto_tail_hash),
        payload_enc = COALESCE(EXCLUDED.payload_enc, marking_codes.payload_enc),
        last_seen_at = now()
    RETURNING id INTO v_code;

    INSERT INTO code_operations (
        org_id, code_id, user_id, device_id, printer_id, kind, duration_ms, error_code
    )
    VALUES (
        p_org_id, v_code, p_user_id, p_device_id, p_printer_id, p_kind, p_duration_ms, p_error_code
    )
    RETURNING id INTO v_op;

    RETURN v_op;
END;
$$;
