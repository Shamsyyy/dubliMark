# Production deploy (doublemark.ru + VPS)

Target: **DoubleMark** site + API + PostgreSQL on one VPS (Timeweb Cloud).

## Before you start

- VPS public IPv4 (example: `46.149.70.172`)
- Domain **doublemark.ru** registered
- DNS A records (Timeweb → Domains → DNS):

| Host | Type | Value |
|------|------|-------|
| `@` | A | VPS IPv4 |
| `www` | A | VPS IPv4 |
| `api` | A | VPS IPv4 |

Wait 15 min – 24 h. Check: `nslookup doublemark.ru` must return your VPS IP.

**Security:** rotate root password after setup; use SSH keys; never commit `.env`.

---

## 1. Server bootstrap (SSH as root)

```bash
ssh root@YOUR_VPS_IP

apt update && apt upgrade -y
apt install -y git curl nginx certbot python3-certbot-nginx docker.io docker-compose-plugin

systemctl enable docker nginx
systemctl start docker nginx
```

---

## 2. Clone repo and env

```bash
mkdir -p /opt/doublemark
cd /opt/doublemark
git clone https://github.com/YOUR_USER/DubliMark.git .
# or upload project via scp/rsync from your PC

cp .env.production.example .env
nano .env   # set POSTGRES_PASSWORD and Jwt__SigningKey (32+ chars)
```

Generate secrets:

```bash
openssl rand -base64 32   # POSTGRES_PASSWORD
openssl rand -base64 48   # Jwt__SigningKey
```

---

## 3. Start API + Postgres

```bash
cd /opt/doublemark
docker compose -f docker-compose.prod.yml up -d --build
curl -s http://127.0.0.1:5080/health
```

---

## 4. Nginx

```bash
cp /opt/doublemark/deploy/nginx/doublemark.ru.conf /etc/nginx/sites-available/doublemark.ru
ln -sf /etc/nginx/sites-available/doublemark.ru /etc/nginx/sites-enabled/
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
```

---

## 5. Build and upload site (from your PC)

```powershell
cd C:\Projects\DubliMarkSite
npm ci
$env:VITE_BASE_PATH="/"
$env:VITE_BACKEND="local"
$env:VITE_API_BASE_URL="https://api.doublemark.ru"
npx vite build

scp -r dist/* root@YOUR_VPS_IP:/var/www/doublemark/
```

On server once:

```bash
mkdir -p /var/www/doublemark
chown -R www-data:www-data /var/www/doublemark
```

---

## 6. HTTPS (Let's Encrypt)

DNS must already point to the VPS.

```bash
certbot --nginx -d doublemark.ru -d www.doublemark.ru -d api.doublemark.ru
```

Renewal is automatic via certbot timer.

---

## 7. Smoke test

- https://doublemark.ru — site opens
- https://api.doublemark.ru/health — `{"status":"ok",...}`
- Register on site → login in Desktop with `Backend: LocalApi` and `ApiBaseUrl: https://api.doublemark.ru`

---

## 8. Desktop release

Rebuild installer with production API URL in `appsettings.json` next to exe or default in `BackendConfigLoader`.

Publish `updates/update.json` and installer to `https://doublemark.ru/downloads/` and `https://doublemark.ru/updates/`.

---

## Useful commands

```bash
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml exec postgres psql -U doublemark -d doublemark -c "SELECT email FROM profiles;"
```
