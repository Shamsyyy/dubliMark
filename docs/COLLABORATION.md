# Совместная разработка DoubleMark (Git)

Репозиторий: [Shamsyyy/dubliMark](https://github.com/Shamsyyy/dubliMark)  
Ветка по умолчанию: **main**  
Прямой push в **main** запрещён — только через Pull Request.

---

## 1. Первый запуск (один раз)

### Доступ к репозиторию

1. Другой разработчик добавляет ваш GitHub-аккаунт в **Collaborators** (Settings → Collaborators).
2. Примите приглашение: письмо на почту или [github.com/notifications](https://github.com/notifications).
3. Убедитесь, что в браузере и в CLI залогинены **одним и тем же** аккаунтом:
   ```powershell
   gh auth status
   ```

Репозиторий **не появится** в списке «ваших» на GitHub — он остаётся у владельца. Открывайте по ссылке: `https://github.com/Shamsyyy/dubliMark`.

### Клонирование

Клонируйте **в корень рабочей папки**, без вложенной подпапки с тем же именем:

```powershell
cd D:\
git clone https://github.com/Shamsyyy/dubliMark.git DubliMark
cd DubliMark
```

Не делайте `git clone` внутрь уже существующего `DubliMark\dubliMark\` — Cursor будет открывать старую копию без файлов с GitHub.

### Git-идентификация (один раз на машине)

```powershell
git config user.name "Ваше Имя"
git config user.email "ваш@email.com"
```

### Зависимости

Основной проект — .NET:

```powershell
dotnet restore
dotnet build
dotnet test
```

`npm install` нужен **только** для веб-проекта (например `dublimark-site`), если вы работаете с ним отдельно. В репозитории приложения WPF npm не используется.

### Секреты и локальные файлы

- Не коммитьте `.env`, пароли, ключи Supabase, реальные коды Честного ЗНАКа.
- Локальные настройки — в `appsettings.local.json` / `.env.local` (они в `.gitignore`).

---

## 2. Рабочий цикл (каждая задача)

**Правило:** одна задача → одна ветка → несколько осмысленных коммитов → один Pull Request в `main`.

### Шаг 0 — перед любыми изменениями

Всегда начинайте с актуального `main`:

```powershell
git checkout main
git pull origin main
```

Если есть незакоммиченные правки:

```powershell
git stash push -u -m "wip before switch"
git checkout main
git pull origin main
git checkout -b feature/название-задачи
git stash pop
```

### Шаг 1 — новая ветка

Имя ветки: латиница, через дефис, с префиксом:

```powershell
git checkout -b feature/scanner-hid-autobind
# или: fix/print-margin, chore/release-script
```

Примеры: `feature/scan-history`, `fix/gtin-warning`, `chore/docs-git-workflow`.

### Шаг 2 — разработка и коммиты

- Делайте **отдельный коммит** на каждый логический шаг (не один коммит на всю неделю).
- Сообщения — на русском или английском, по смыслу задачи:

```powershell
git add path/to/file.cs
git commit -m "fix: не показывать предупреждение GS для короткого HID-скана"
```

Перед push желательно проверить сборку:

```powershell
dotnet build
dotnet test
```

### Шаг 3 — отправка ветки

```powershell
git push -u origin feature/название-задачи
```

**В `main` не пушить:**

```powershell
# ЗАПРЕЩЕНО для обычной работы:
git push origin main
```

### Шаг 4 — Pull Request

1. GitHub → **Compare & pull request** (или `gh pr create`).
2. Base: **main** ← Compare: **ваша ветка**.
3. Кратко опишите: что сделано, как проверить.
4. Дождитесь ревью, исправьте замечания **в той же ветке** (новые коммиты + `git push`).
5. После merge удалите ветку (кнопка на GitHub или `git branch -d feature/...`).

Создание PR из CLI:

```powershell
gh pr create --base main --head feature/название-задачи --title "Краткий заголовок" --body "## Summary`n- ...`n`n## Test plan`n- [ ] dotnet test`n- [ ] ручная проверка скана"
```

### Шаг 5 — после merge

```powershell
git checkout main
git pull origin main
git branch -d feature/название-задачи
```

---

## 3. Если main ушёл вперёд, пока вы в ветке

Перед PR подтяните свежий `main` в свою ветку:

```powershell
git checkout feature/название-задачи
git fetch origin
git merge origin/main
# при конфликтах — исправить файлы, затем:
git add .
git commit -m "merge: sync with main"
git push
```

Альтернатива (линейная история): `git rebase origin/main` — только если команда договорилась использовать rebase.

---

## 4. Конфликты при pull / merge

1. Git пометит файлы как conflicted.
2. Откройте файлы, найдите маркеры `<<<<<<<`, выберите нужный код.
3. Завершите:

```powershell
git add .
git commit -m "merge: resolve conflicts with main"
```

Не коммитьте файлы с неразрешёнными маркерами конфликта.

---

## 5. Чеклист перед Pull Request

| Проверка | Команда / действие |
|----------|-------------------|
| Актуальный `main` влит в ветку | `git merge origin/main` |
| Сборка | `dotnet build` |
| Тесты | `dotnet test` |
| Нет секретов в diff | просмотр `git diff` |
| Нет лишних файлов (`dist/`, `artifacts/`) | не `git add .` слепо — смотрите `git status` |
| Ветка не `main` | `git branch` |
| Push в свою ветку | `git push origin feature/...` |

---

## 6. Частые ошибки

| Проблема | Причина | Решение |
|----------|---------|---------|
| Файлы с GitHub не видны в Cursor | Не делали `git pull` в корне проекта | `git checkout main` → `git pull origin main` |
| Два набора файлов | Клон в `DubliMark\dubliMark\` | Работать только в корне `DubliMark\`, вложенный клон удалить |
| Репо нет в «моих» на GitHub | Вы collaborator, не владелец | Открыть `github.com/Shamsyyy/dubliMark` |
| Push в main отклонён | Защита ветки (рекомендуется включить) | PR из `feature/*` |
| Огромный PR | Ветка жила неделями | Чаще мержить `main`, дробить задачи |

---

## 7. Рекомендуемые настройки репозитория (для владельца)

Имеет смысл включить на GitHub:

- **Branch protection** для `main`: require PR, 1 approval (по желанию), запрет force-push.
- **Delete head branches** после merge.
- Статус CI (когда появится): required check перед merge.

---

## 8. Краткая шпаргалка

```powershell
# Начало задачи
git checkout main
git pull origin main
git checkout -b feature/моя-задача

# Работа
# ... правки ...
dotnet test
git add .
git commit -m "feat: описание"
git push -u origin feature/моя-задача

# PR на GitHub → review → merge

# После merge
git checkout main
git pull origin main
git branch -d feature/моя-задача
```

---

## 9. Связанные документы

- [RELEASE.md](./RELEASE.md) — сборка релиза и установщика.
- [AGENTS.md](../AGENTS.md) — правила кода (GS1, сканер, тесты).
