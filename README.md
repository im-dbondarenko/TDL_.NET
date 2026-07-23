# Todo API (.NET)

REST API + UI for managing todos. Clean Architecture, CQRS (MediatR), FluentValidation + ValidationBehavior pipeline, EF Core + SQLite. Guid-based IDs.

## Structure

```
Source/
  TodoApi.Domain/          -- Entities
  TodoApi.Application/     -- Commands, queries, validators, ValidationBehavior (Todos/)
  TodoApi.Infrastructure/  -- EF Core, SQLite, repository
  TodoApi.API/             -- REST controller, DI, Swagger
Tests/
  TodoApi.Tests/           -- xUnit + NSubstitute
Web/
  index.html               -- UI (vanilla JS)
docs/                      -- Architecture, build guide, validation, Skynet comparison
```

## Running

```bash
# API (port 5279)
cd Source/TodoApi.API
dotnet run

# UI (port 3000)
cd Web
python3 -m http.server 3000

# Tests
dotnet test
```

- **UI:** http://localhost:3000
- **Swagger:** http://localhost:5279/swagger
- **DB:** `todo.db` creates automatically on first startup

## Key Features

- **Guid IDs** — безопасные, неугадываемые, генерируются на клиенте (`Guid.NewGuid()`)
- **Server-side validation only** — FluentValidation + shared rules (`TodoValidationRules.cs`), Latin-only regex
- **ValidationBehavior** — MediatR pipeline автоматически запускает валидацию ДО handler'а
- **Exception Handler** — `ValidationException` → HTTP 400 с JSON `{ errors: { field: ["message"] } }`
- **UI показывает ошибки сервера** — никакой клиентской валидации

## Docs

- [Архитектура](docs/ARCHITECTURE.md) — Clean Architecture, CQRS, MediatR, EF Core
- [Пошаговая сборка](docs/BUILD_GUIDE.md) — как собрать проект с нуля
- [Валидация](docs/VALIDATION.md) — как устроена валидация (pipeline, shared rules, ошибки)
- [Сравнение со Skynet](docs/SKYNET_COMPARISON.md) — что общего с production Lender
