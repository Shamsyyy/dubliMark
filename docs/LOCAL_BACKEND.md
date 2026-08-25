# Local backend (DoubleMark API + PostgreSQL)

## 1. Start Postgres

1. Start **Docker Desktop**
2. From repo root:

```powershell
cd C:\Projects\DubliMark
copy .env.example .env
docker compose up -d
docker compose ps
```

Postgres: `localhost:5432`  
DB/user/password: see `.env` (defaults in `.env.example`)

## 2. Run API

```powershell
cd C:\Projects\DubliMark
dotnet run --project src\DoubleMark.Api
```

- Swagger: http://localhost:5080/swagger  
- Health: http://localhost:5080/health  

## 3. Desktop app

Desktop uses the DoubleMark API only (`http://localhost:5080` locally, `https://api.doublemark.ru` in production).

Optional `appsettings.local.json` in repo root or next to the exe:

```json
{
  "Backend": {
    "ApiBaseUrl": "http://localhost:5080"
  }
}
```

## 4. Create a user

Ways to register (same API / same DB):

1. **Desktop** — кнопка «Создать аккаунт» на экране входа (LocalApi)
2. **Сайт** — `C:\Projects\DubliMarkSite` (см. ниже)
3. **HTTP**:

```powershell
Invoke-RestMethod http://localhost:5080/api/auth/register -Method Post -ContentType application/json -Body '{"email":"dev@doublemark.local","password":"dev12345"}'
```

Local registration creates an **active** subscription (`local-dev`) for development.

## 4b. Marketing site (DubliMarkSite)

```powershell
cd C:\Projects\DubliMarkSite
copy .env.example .env.local   # if missing
# VITE_BACKEND=local
# VITE_API_BASE_URL=http://localhost:5080
npm install
npm run dev
```

Откройте Vite URL (обычно http://localhost:5173). В шапке есть **Регистрация** → `/register`. После регистрации тот же аккаунт логинится в Desktop.

## 5. Useful endpoints

| Method | Path |
|--------|------|
| POST | `/api/auth/register` |
| POST | `/api/auth/login` |
| POST | `/api/auth/refresh` |
| GET | `/api/me/profile` |
| GET | `/api/me/subscription` |
| GET/PUT | `/api/me/templates` |
| GET/POST | `/api/me/scan-history` |

## Notes

- Tokens on the desktop are stored with **DPAPI** (`%AppData%\DoubleMark\api-session.bin`)
- Do not commit `.env` or real JWT secrets
- This is for **local development**; production will use a VPS + HTTPS later
