#!/usr/bin/env python3
"""Write pgweb env vars into /opt/doublemark/.env. Prints only web login."""
import pathlib
import re
import secrets
import urllib.parse

env_path = pathlib.Path("/opt/doublemark/.env")
text = env_path.read_text()


def get(key, default=None):
    match = re.search(rf"^{re.escape(key)}=(.*)$", text, re.M)
    if not match:
        return default
    value = match.group(1).strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
        value = value[1:-1]
    return value


def upsert(key, value):
    global text
    line = f"{key}={value}"
    if re.search(rf"^{re.escape(key)}=", text, re.M):
        text = re.sub(rf"^{re.escape(key)}=.*$", line, text, count=1, flags=re.M)
    else:
        if not text.endswith("\n"):
            text += "\n"
        text += line + "\n"


user = get("POSTGRES_USER", "doublemark")
password = get("POSTGRES_PASSWORD")
database = get("POSTGRES_DB", "doublemark")
if not password:
    raise SystemExit("POSTGRES_PASSWORD missing")

url = (
    f"postgres://{user}:{urllib.parse.quote(password, safe='')}@postgres:5432/"
    f"{database}?sslmode=disable"
)
auth_user = get("PGWEB_AUTH_USER") or "dbadmin"
auth_pass = get("PGWEB_AUTH_PASS") or secrets.token_urlsafe(14)
upsert("PGWEB_DATABASE_URL", url)
upsert("PGWEB_AUTH_USER", auth_user)
upsert("PGWEB_AUTH_PASS", auth_pass)
env_path.write_text(text)
print(f"PGWEB_AUTH_USER={auth_user}")
print(f"PGWEB_AUTH_PASS={auth_pass}")
