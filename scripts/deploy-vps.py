#!/usr/bin/env python3
"""One-shot DoubleMark VPS deploy. Requires DM_DEPLOY_PASSWORD env var."""
from __future__ import annotations

import os
import sys
import time
import paramiko

HOST = os.environ.get("DM_DEPLOY_HOST", "46.149.70.172")
USER = os.environ.get("DM_DEPLOY_USER", "root")
PASSWORD = os.environ.get("DM_DEPLOY_PASSWORD", "")
REPO = "https://github.com/Shamsyyy/dubliMark.git"
CERT_EMAIL = os.environ.get("DM_CERT_EMAIL", "daur20011@mail.ru")


def run(client: paramiko.SSHClient, cmd: str, timeout: int = 900) -> tuple[int, str, str]:
    print(f"\n$ {cmd}")
    _, stdout, stderr = client.exec_command(cmd, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    code = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.rstrip())
    if err.strip():
        print(err.rstrip(), file=sys.stderr)
    return code, out, err


def main() -> int:
    if not PASSWORD:
        print("Set DM_DEPLOY_PASSWORD", file=sys.stderr)
        return 1

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)

    commands = [
        "export DEBIAN_FRONTEND=noninteractive",
        "apt-get update -y",
        "apt-get install -y git curl nginx certbot python3-certbot-nginx docker.io docker-compose",
        "systemctl enable docker nginx",
        "systemctl start docker nginx",
        "mkdir -p /opt/doublemark /var/www/doublemark",
        "if [ -d /opt/doublemark/.git ]; then cd /opt/doublemark && git pull origin main; else git clone "
        + REPO
        + " /opt/doublemark; fi",
        "cd /opt/doublemark && if [ ! -f .env ]; then python3 -c \"from pathlib import Path; import secrets; t=Path('.env.production.example').read_text(); t=t.replace('CHANGE_ME_strong_db_password', secrets.token_urlsafe(24)); t=t.replace('CHANGE_ME_at_least_32_random_characters_long', secrets.token_urlsafe(48)); Path('.env').write_text(t)\"; fi",
        "cd /opt/doublemark && docker-compose -f docker-compose.prod.yml up -d --build",
        "sleep 5 && curl -sf http://127.0.0.1:5080/health",
        "cp /opt/doublemark/deploy/nginx/doublemark.ru.conf /etc/nginx/sites-available/doublemark.ru",
        "ln -sf /etc/nginx/sites-available/doublemark.ru /etc/nginx/sites-enabled/doublemark.ru",
        "rm -f /etc/nginx/sites-enabled/default",
        "nginx -t && systemctl reload nginx",
    ]

    for cmd in commands:
        code, _, _ = run(client, cmd)
        if code != 0:
            print(f"FAILED ({code}): {cmd}", file=sys.stderr)
            client.close()
            return code

    # certbot may fail if DNS not propagated yet
    cert_cmd = (
        f"certbot --nginx --non-interactive --agree-tos --email {CERT_EMAIL} "
        "-d doublemark.ru -d www.doublemark.ru -d api.doublemark.ru --redirect || true"
    )
    run(client, cert_cmd, timeout=300)

    run(client, "curl -sf http://127.0.0.1:5080/health || true")
    run(client, "curl -sfI http://doublemark.ru | head -n 1 || true")
    run(client, "curl -sfI http://api.doublemark.ru/health | head -n 1 || true")

    client.close()
    print("\nDeploy script finished.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
