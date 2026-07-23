# Todo API (.NET)

REST API + UI for managing todos. Clean Architecture, CQRS (MediatR), EF Core + SQLite.

## Structure

```
Source/
  TodoApi.Domain/          -- Entities
  TodoApi.Application/     -- Commands, queries, validators (Todos/)
  TodoApi.Infrastructure/  -- EF Core, SQLite, repository
  TodoApi.API/             -- REST controller, DI, Swagger
Tests/
  TodoApi.Tests/           -- xUnit + NSubstitute
Web/
  index.html               -- UI (vanilla JS)
docs/                      -- Architecture, build guide, Skynet comparison
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
