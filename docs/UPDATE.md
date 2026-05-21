# Публикация обновлений DoubleMark

## 1. Увеличить версию

В `Directory.Build.props`:

```xml
<Version>2.1.1</Version>
<AssemblyVersion>2.1.1.0</AssemblyVersion>
<FileVersion>2.1.1.0</FileVersion>
```

## 2. Собрать установщик и update.json

```powershell
cd C:\Projects\DubliMark
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Результат:

- `dist\installer\DoubleMarkSetup-<version>-<buildId>.exe`
- `dist\updates\update.json` (с SHA256 и installerUrl)

## 3. Загрузить на сайт

На GitHub Pages проекта `doublemarksite`:

```text
public/downloads/DoubleMarkSetup-2.1.1.exe
public/updates/update.json
```

Публичные URL:

- `https://shamsyyy.github.io/doublemarksite/downloads/DoubleMarkSetup-2.1.1.exe`
- `https://shamsyyy.github.io/doublemarksite/updates/update.json`

## 4. Проверить в приложении

1. Откройте **Личный кабинет → Приложение и обновления**.
2. Нажмите **Проверить обновления**.
3. Если `update.json` новее текущей версии — появится предложение обновиться.
4. **Скачать и установить** — скачает EXE, проверит SHA256, запустит установщик.

## Формат update.json

См. `updates/update.example.json`.

Обязательные поля:

- `version`
- `installerUrl` (только HTTPS, доверенные домены)
- `sha256` (hex, без пробелов)

## Безопасность

- В приложении нет GitHub token и `service_role`.
- Установщик скачивается только с `shamsyyy.github.io` / `github.com` / `githubusercontent.com`.
- Перед запуском проверяется SHA256.
- При несовпадении hash файл удаляется.

## Обязательное обновление

```json
"mandatory": true,
"minSupportedVersion": "2.1.0"
```

Если текущая версия ниже `minSupportedVersion` или `mandatory=true` при наличии новой версии — рабочие функции блокируются до обновления.
