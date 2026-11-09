# Публикация LoadLens на GitHub

## Статус

Проект опубликован: **<https://github.com/Sgushenka77/chupep>**

- Ветка по умолчанию: `main`.
- Remote: `origin` → `https://github.com/Sgushenka77/chupep.git`.
- CI-workflow собирает проект, запускает сценарные тесты и публикует artifact `LoadLens-win-x64`.

## Что важно знать

- В `C:\Users\student\source\someApp` находится **отдельный** git-репозиторий.
- Это важно: папка лежит внутри большого репозитория `C:\Users\student`, и коммитить весь профиль пользователя нельзя.
- Проверить, что вы находитесь именно в проекте, а не в домашней папке:

```powershell
cd C:\Users\student\source\someApp
git rev-parse --show-toplevel
# должно вывести: C:/Users/student/source/someApp
```

## Как обновить репозиторий

```powershell
cd C:\Users\student\source\someApp
git status
git add .
git commit -m "краткое описание изменений"
git push
```

Перед сборкой закройте запущенный LoadLens: Windows блокирует DLL в `bin`.

## Если нужно пересоздать remote

```powershell
git remote set-url origin https://github.com/Sgushenka77/chupep.git
# или, если remote отсутствует:
git remote add origin https://github.com/Sgushenka77/chupep.git
git branch -M main
git push -u origin main
```

## GitHub Actions

Workflow `.github/workflows/build.yml` запускается на каждый push в `main`:

1. собирает решение на `windows-latest`;
2. запускает сценарные тесты `LoadLens.DiagHarness`;
3. публикует framework-dependent сборку в artifact `LoadLens-win-x64`.

Страница запусков: <https://github.com/Sgushenka77/chupep/actions>.

Для загрузки workflow-файла GitHub требует OAuth-токен со scope `workflow`; он уже выдан в этой сессии. При повторной авторизации:

```powershell
gh auth refresh --hostname github.com --scopes workflow
```

## Если что-то пошло не так

- `remote origin already exists` — используйте `git remote set-url origin ...`.
- `repository not found` — проверьте логин, имя репозитория и права.
- `src refspec main does not match any` — сначала нужен коммит: `git status`, `git add .`, `git commit -m "..."`.
- Случайно оказались в родительском репозитории — не делайте `git add -A`; вернитесь в `C:\Users\student\source\someApp` и проверьте `git rev-parse --show-toplevel`.
- `refusing to allow an OAuth App to create or update workflow` — выполните `gh auth refresh --hostname github.com --scopes workflow`.

