# Публикация LoadLens на GitHub

## Что уже сделано

- В `C:\Users\student\source\someApp` создан **отдельный** git-репозиторий.
- Это важно: папка находится внутри большого репозитория `C:\Users\student`, и коммитить весь профиль пользователя нельзя.
- Добавлены `.gitignore`, `LICENSE`, CI-workflow и этот документ.
- Локальный коммит создаётся в конце работы; remote `origin` на GitHub пока не добавлен.

Проверить, что вы находитесь именно в проекте, а не в домашней папке:

```powershell
cd C:\Users\student\source\someApp
git rev-parse --show-toplevel
# должно вывести: C:/Users/student/source/someApp
```

## Вариант A. Через сайт GitHub и git (без установки gh)

1. Откройте <https://github.com/new>.
2. Имя репозитория: `LoadLens` (или любое другое).
3. **Не** добавляйте README, .gitignore и лицензию — они уже есть в проекте.
4. Нажмите **Create repository**.
5. Выполните команды, подставив свой логин и имя репозитория:

```powershell
cd C:\Users\student\source\someApp
git remote add origin https://github.com/ВАШ_ЛОГИН/LoadLens.git
git branch -M main
git push -u origin main
```

При первом `push` Git Credential Manager откроет браузер и попросит войти в GitHub. Пароль от аккаунта вводить в терминал не нужно — используется OAuth-вход.

Если remote уже был добавлен с другим адресом:

```powershell
git remote set-url origin https://github.com/ВАШ_ЛОГИН/LoadLens.git
```

## Вариант B. Через GitHub CLI

```powershell
winget install --id GitHub.cli
gh auth login
cd C:\Users\student\source\someApp
gh repo create LoadLens --public --source . --remote origin --push
```

`gh auth login` тоже открывает браузер. Токен в чат и в файлы репозитория лучше не вставлять.

## Что произойдёт после push

Workflow `.github/workflows/build.yml` на каждом push в `main`:

1. соберёт решение на `windows-latest`;
2. запустит сценарные тесты `LoadLens.DiagHarness`;
3. опубликует framework-dependent сборку в artifact `LoadLens-win-x64`.

Страница запусков: вкладка **Actions** в репозитории. Artifact можно скачать из завершённого run.

## Если нужно, чтобы запушил я

Мне нужен один из двух вариантов:

1. **GitHub CLI уже авторизован** — скажите имя репозитория и видимость (public/private), я выполню `gh repo create ... --push`.
2. **Вы создали пустой репозиторий** — пришлите его HTTPS-URL, я добавлю remote. Для самого push всё равно понадобится ваша авторизация в Git Credential Manager, если она ещё не сохранена.

Я не запрашиваю пароль или personal access token в переписке. Безопаснее авторизоваться самостоятельно через `gh auth login` или Credential Manager.

## Если что-то пошло не так

- `remote origin already exists` — используйте `git remote set-url origin ...`.
- `repository not found` — проверьте логин, имя репозитория и права.
- `src refspec main does not match any` — сначала нужен коммит: `git status`, `git add .`, `git commit -m "..."`.
- Случайно оказались в родительском репозитории — не делайте `git add -A`; вернитесь в `C:\Users\student\source\someApp` и проверьте `git rev-parse --show-toplevel`.
