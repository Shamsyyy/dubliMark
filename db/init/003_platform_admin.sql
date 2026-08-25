-- Platform admins (profiles.role). Idempotent.
UPDATE profiles
SET role = 'admin',
    updated_at = now()
WHERE lower(email) = 'daur2001@mail.ru';
