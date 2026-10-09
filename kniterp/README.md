# knitERP

ERP для трикотажного производства. Первый клиент и пилот — ООО «Солвер» (фабрика KS).

Код живёт в папке `kniterp/` и не зависит от Android-приложения и PHP-сервера Staff, которые лежат в корне репозитория.

## Состояние

| Срез | Статус |
|---|---|
| Каркас решения .NET 10, CI со сборкой и тестами на SQL Server | готово |
| Ядро доступа (ТЗ §69): организация, пользователи, роли P0, назначения прав, аудит | доменная модель, сервисы и тесты |
| Вход: пароль (хеш Identity), приглашение 72 ч, 2FA для Владельца и Администратора, блокировка после 5 неудач, журнал входов | готово, ADR-0002 |
| Миграции EF Core вместо EnsureCreated, SQL-скрипт миграций в CI | готово |
| Формы выдачи доступа в Blazor по прототипу | следующий шаг |
| Структура фабрики, справочники, склад | по плану 115 дней |

Кликабельный прототип экранов ядра доступа опубликован отдельно для обратной связи владельца продукта.

## Стек

C# / .NET 10, ASP.NET Core, Blazor Web App (interactive server), EF Core 10, Microsoft SQL Server. Модульный монолит.
Обоснование — `docs/adr/0001-modular-monolith.md`.

## Структура

```
kniterp/
  src/KnitErp.Domain           сущности и правила без зависимостей (матрица P0, расчёт прав, ИНН)
  src/KnitErp.Application      сервисы сценариев, проверка прав на сервере, исключения 403/404/409
  src/KnitErp.Infrastructure   EF Core, SQL Server, неизменяемый журнал аудита
  src/KnitErp.Web              Blazor Web App
  tests/KnitErp.UnitTests      быстрые тесты правил
  tests/KnitErp.IntegrationTests  SQL Server: изоляция организаций, аудит, транзакции
  docs/                        ADR, журнал решений, граница MVP
```

## Как собрать

```bash
cd kniterp
dotnet build
dotnet test tests/KnitErp.UnitTests
# интеграционные тесты нужен SQL Server:
export KNITERP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=<пароль>;TrustServerCertificate=true"
dotnet test tests/KnitErp.IntegrationTests
```

Без переменной `KNITERP_TEST_SQL` интеграционные тесты пропускаются.

### Схема базы и миграции

Схема создаётся только миграциями EF Core (`src/KnitErp.Infrastructure/Persistence/Migrations`). Применённая миграция не редактируется: любое изменение оформляется новой миграцией.

```bash
cd kniterp
dotnet tool restore
# после изменения сущности или конфигурации:
dotnet ef migrations add <ИмяИзменения> -p src/KnitErp.Infrastructure -o Persistence/Migrations
```

Тест `Model_has_no_changes_missing_from_migrations` падает, если модель изменена, а миграция не создана.

Как обновляется схема:
- **Development**: миграции применяются при запуске.
- **Рабочая среда**: приложение само схему не меняет. При неприменённых миграциях оно не запускается и называет их в ошибке. Обновить схему можно двумя способами:
  - `dotnet KnitErp.Web.dll migrate`: применить миграции и выйти;
  - SQL-скрипт `kniterp-migrations.sql` из артефактов CI. Скрипт идемпотентный, его можно передать администратору БД и запускать повторно.

База разработки, созданная до перехода на миграции через `EnsureCreated`, не имеет истории миграций. Её нужно один раз удалить.

### Первый вход при локальном запуске

```bash
cd kniterp/src/KnitErp.Web
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__KnitErp="Server=localhost,1433;Database=kniterp_dev;User Id=sa;Password=<пароль>;TrustServerCertificate=true" dotnet run
```

При первом запуске создаётся тестовая организация, а в лог выводится ссылка `/account/invite?token=…` для установки пароля Владельца (`owner@kniterp.local`). После пароля система попросит подключить приложение-аутентификатор: Владелец без 2FA не входит. В GitHub Actions SQL Server поднимается автоматически (`.github/workflows/kniterp.yml`).

## Правила разработки

См. `CLAUDE.md` и ТЗ §14.15–14.18: один вертикальный срез за раз, права проверяются на сервере, применённые миграции не правятся, секреты и персональные данные в репозиторий не попадают.
