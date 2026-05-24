# DoubleMark — безопасный релиз Windows

Документ для подготовки публичной установки с [doublemark.ru](https://doublemark.ru): Authenticode, проверка обновлений, снижение ложных срабатываний Defender.

## Выбранный формат установки

**Inno Setup EXE** (`installer/DoubleMark.iss`), не MSIX.

| Критерий | Inno EXE | MSIX |
|----------|----------|------|
| WPF + self-contained single-file | Проверенный путь | Требует упаковки, сертификат Store/код-подпись |
| COM/HID/Raw Input, печать | Без изменений | Доп. capability / риск регрессий |
| Подпись Authenticode | Стандарт для `.exe` установщика | Обязательна для sideload |
| Распространение с сайта | Прямая ссылка на `.exe` | Сложнее для B2B |

Установка: `%ProgramFiles%\DoubleMark` (или `%LocalAppData%\Programs` при `PrivilegesRequired=lowest`). Удаление через «Приложения и компоненты». После установки приложение **не** запускается из `%TEMP%`.

## Права и поведение

- `app.manifest`: `asInvoker` — **без** UAC «Запуск от имени администратора».
- Нет автозапуска, служб, драйверов, правок реестра вне стандартного uninstall Inno.
- Обновления: HTTPS manifest → скачивание в `%LocalAppData%\DoubleMark\Updates\` → SHA-256 → Authenticode → запуск установщика через `Process.Start` (без PowerShell/cmd/bat).

## Какие файлы подписывать

После `scripts/build-release.ps1` и `scripts/build-installer.ps1`:

| Файл | Обязательно |
|------|-------------|
| `dist\DoubleMark\DoubleMark.exe` | Да |
| `dist\installer\DoubleMarkSetup-*.exe` | Да |
| `dist\DoubleMark\*.dll` (если folder publish) | Да, все production DLL |

Не подписывать и не публиковать: `*.pdb`, `.env*`, `appsettings.local.json`, тестовые exe, скрипты сборки, `obfuscar` output без отдельного QA.

## Когда применять подпись

1. `dotnet publish` / Inno — **без** сертификата в репозитории.
2. **Подписать** `DoubleMark.exe` и все DLL в `dist\DoubleMark\` (если не single-file).
3. Собрать Inno (`build-installer.ps1`).
4. **Подписать** `DoubleMarkSetup-*.exe`.
5. Пересчитать SHA-256 установщика → обновить `updates/update.json` на сайте.
6. `scripts/verify-release-signatures.ps1` — CI/локально перед выкладкой.

Переменные окружения (только на машине сборки, не в git):

- `SIGN_CERT_PATH` — путь к `.pfx`
- `SIGN_CERT_PASSWORD` — пароль PFX

Скрипты вызывают `signtool sign` при наличии этих переменных.

## SignTool — примеры

```powershell
# Подпись приложения
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD `
  dist\DoubleMark\DoubleMark.exe

# Подпись установщика
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD `
  dist\installer\DoubleMarkSetup-2.1.3.exe

# Проверка
signtool verify /pa /v dist\DoubleMark\DoubleMark.exe
signtool verify /pa /v dist\installer\DoubleMarkSetup-2.1.3.exe
```

Или:

```powershell
.\scripts\verify-release-signatures.ps1 `
  -ReleaseRoot dist\DoubleMark `
  -InstallerPath dist\installer\DoubleMarkSetup-2.1.3.exe
```

Для внутренних сборок без сертификата:

```powershell
.\scripts\verify-release-signatures.ps1 -AllowUnsigned ...
```

В приложении для теста неподписанных обновлений (только разработка):

`DOUBLEMARK_ALLOW_UNSIGNED_UPDATES=1`

## Manifest обновлений (`update.json`)

Размещение: `https://doublemark.ru/updates/update.json`

Поля:

| Поле | Назначение |
|------|------------|
| `version` | Версия релиза |
| `publishedAt` | ISO 8601 UTC |
| `downloadUrl` | HTTPS URL установщика (предпочтительно) |
| `installerUrl` | Обратная совместимость |
| `sha256` | SHA-256 установщика (hex lower) |
| `mandatory`, `minSupportedVersion`, `notes` | Политика обновления |

Допустимые хосты загрузки заданы в `UpdateService.AllowedHosts` (`doublemark.ru`, GitHub Pages fallback).

## Секреты — нельзя в репозитории

- PFX / приватные ключи код-подписи
- Пароли PFX, OTP токенов
- Supabase `service_role`, SMTP, webhook secrets
- Реальные коды Честного ЗНАКА в тестах/логах
- `.env`, `appsettings.local.json` с production ключами

В релиз попадает только **anon** ключ Supabase через `appsettings.json` на этапе сборки.

## Снижение AV false positives

- Не использовать UPX, агрессивную обфускацию по умолчанию (`-EnableObfuscation` только осознанно).
- Один основной `DoubleMark.exe`, без лишних вспомогательных exe.
- Обновления не из Temp: `%LocalAppData%\DoubleMark\Updates\`.
- Подписанный установщик и exe.
- Логи Release: уровень ≥ Info, редактирование токенов/сырых payload.
- Токены сессии: DPAPI (`SupabaseSessionStorage`).

## Команды сборки и проверки

```powershell
cd C:\Projects\DubliMark
dotnet restore
dotnet build -c Release
dotnet test

.\scripts\build-release.ps1
.\scripts\build-installer.ps1

.\scripts\verify-release-signatures.ps1 `
  -ReleaseRoot dist\DoubleMark `
  -InstallerPath (Get-ChildItem dist\installer\DoubleMarkSetup-*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

Публикация на сайт:

- `dist\downloads\DoubleMarkSetup-{version}.exe`
- `dist\updates\update.json` → `https://doublemark.ru/updates/update.json`
