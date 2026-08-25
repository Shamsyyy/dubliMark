CREATE TABLE IF NOT EXISTS cookie_consent_events (
  id uuid PRIMARY KEY,
  consent_id text NOT NULL,
  consent_version text NOT NULL,
  client_timestamp_utc timestamptz NOT NULL,
  received_at_utc timestamptz NOT NULL DEFAULT now(),
  necessary boolean NOT NULL DEFAULT true,
  analytics boolean NOT NULL DEFAULT false,
  functional boolean NOT NULL DEFAULT false,
  marketing boolean NOT NULL DEFAULT false,
  action text NOT NULL
);

CREATE INDEX IF NOT EXISTS cookie_consent_events_consent_id_idx
  ON cookie_consent_events (consent_id);

CREATE TABLE IF NOT EXISTS personal_data_consents (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  consent_version text NOT NULL,
  accepted_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS personal_data_consents_user_id_idx
  ON personal_data_consents (user_id);
