# DoubleMark Release Build

## Стек

- **Framework:** .NET 8 (`net8.0-windows`)
- **UI:** WPF
- **Solution:** `DoubleMark.sln`
- **Desktop project:** `src\DoubleMark.Desktop\DoubleMark.Desktop.csproj`
- **Version:** 2.1.0

## Собрать EXE

Из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Результат:

```text
C:\Projects\DubliMark\dist\DoubleMark\DoubleMark.exe
```

Сборка: `win-x64`, self-contained, single-file, без `.pdb`.

Скрипт копирует иконки из `ico\` в `src\DoubleMark.Desktop\Assets\Branding\` и генерирует `dist\DoubleMark\appsettings.json` из `.env.local` / `.env` / `appsettings.local.json` (только Supabase URL и anon key).

Для folder-publish (например, с Obfuscar):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -FolderPublish
```

Публикация обновлений для пользователей: см. [UPDATE.md](UPDATE.md).

## Собрать установщик

Нужен [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Результат:

```text
C:\Projects\DubliMark\dist\installer\DoubleMarkSetup-2.1.0-YYYYMMDD-HHMMSS.exe
```

Имя установщика содержит метку сборки (`buildId`), чтобы отличать свежий installer от старого.

В ЛК (раздел «Приложение») отображаются версия и дата обновления из `buildinfo.json` рядом с `DoubleMark.exe`.

## Иконки

Исходники: `C:\Projects\DubliMark\ico`

- `doublemark-logo.ico` — EXE, окно, taskbar, Alt+Tab, установщик, ярлык
- `doublemark-tray.ico` — трей (если используется)
- `doublemark-logo.png` — UI внутри приложения

## Что нельзя коммитить

- `.env`, `.env.local`
- `appsettings.local.json`, `appsettings.json`
- `secrets.json`
- `*.pfx`, `*.snk`
- `dist/`
- токены и сессии пользователей

Никогда не вшивать `service_role` key.

## Проверка перед отправкой пользователю

1. Запустить `dist\DoubleMark\DoubleMark.exe` — вход в ЛК без ошибки конфигурации Supabase.
2. Собрать установщик и установить на чистой Windows / VM.
3. Проверить иконку в окне, taskbar, Alt+Tab, ярлыке.
4. Проверить подписку, COM, HID/RawInput, печать, автопечать, шаблоны.
5. Убедиться, что в `dist\DoubleMark` нет `.pdb`, `.env`, `secrets.json`.

## Code signing (опционально)

```powershell
$env:SIGN_CERT_PATH="C:\secure\codesign.pfx"
$env:SIGN_CERT_PASSWORD="certificate-password"
```

Сертификат хранить вне репозитория.
