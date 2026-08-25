ALTER TABLE users
  ADD COLUMN IF NOT EXISTS email_confirmed_at timestamptz NULL;

CREATE TABLE IF NOT EXISTS auth_action_tokens (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
  token_type text NOT NULL,
  token_hash text NOT NULL UNIQUE,
  expires_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  used_at timestamptz NULL
);

CREATE INDEX IF NOT EXISTS auth_action_tokens_user_type_idx
  ON auth_action_tokens (user_id, token_type);
