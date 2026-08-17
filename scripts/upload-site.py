#!/usr/bin/env python3
"""Upload built site dist/ to VPS /var/www/doublemark."""
from __future__ import annotations

import os
import sys
from pathlib import Path

import paramiko

HOST = os.environ.get("DM_DEPLOY_HOST", "46.149.70.172")
USER = os.environ.get("DM_DEPLOY_USER", "root")
PASSWORD = os.environ.get("DM_DEPLOY_PASSWORD", "")
LOCAL_DIST = Path(os.environ.get("DM_SITE_DIST", r"C:\Projects\DubliMarkSite\dist"))


def upload_dir(sftp: paramiko.SFTPClient, local: Path, remote: str) -> None:
    for path in local.rglob("*"):
        rel = path.relative_to(local).as_posix()
        remote_path = f"{remote}/{rel}" if rel != "." else remote
        if path.is_dir():
            try:
                sftp.mkdir(remote_path)
            except OSError:
                pass
        else:
            remote_parent = os.path.dirname(remote_path)
            parts = remote_parent.split("/")
            cur = ""
            for part in parts:
                cur = f"{cur}/{part}" if cur else part
                try:
                    sftp.mkdir(cur)
                except OSError:
                    pass
            print(f"upload {rel}")
            sftp.put(str(path), remote_path)


def main() -> int:
    if not PASSWORD:
        print("Set DM_DEPLOY_PASSWORD", file=sys.stderr)
        return 1
    if not LOCAL_DIST.exists():
        print(f"Missing {LOCAL_DIST}", file=sys.stderr)
        return 1

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, username=USER, password=PASSWORD, timeout=30)
    client.exec_command("mkdir -p /var/www/doublemark && chown -R www-data:www-data /var/www/doublemark")
    sftp = client.open_sftp()
    upload_dir(sftp, LOCAL_DIST, "/var/www/doublemark")
    sftp.close()
    client.exec_command("chown -R www-data:www-data /var/www/doublemark")
    client.close()
    print("Site uploaded.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
