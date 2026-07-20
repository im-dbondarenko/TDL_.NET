# Todo API (.NET)

A learning project implementing a REST API for managing todos, built with ASP.NET Core following Clean Architecture, CQRS (MediatR), and patterns used in production Skynet microservices.

## Architecture

```
Source/
  TodoApi.Domain/          -- Entities (no dependencies)
  TodoApi.Application/     -- CQRS commands/queries, validators, repository interface
  TodoApi.Infrastructure/  -- EF Core DbContext, SQLite, repository implementation
  TodoApi.API/             -- REST controller, DI registration, Swagger

Tests/
  TodoApi.Tests/           -- xUnit + NSubstitute unit tests
```

**Dependency flow:** API -> Infrastructure -> Application -> Domain

Each layer only references the one below it. Domain has zero dependencies. Application defines interfaces (`ITodoRepository`) that Infrastructure implements — this is the Dependency Inversion Principle.

## Tech Stack

| What | Package |
|---|---|
| Framework | ASP.NET Core 10 |
| CQRS / Mediator | MediatR 14 |
| Validation | FluentValidation 12 |
| ORM | EF Core 10 + SQLite |
| Tests | xUnit + NSubstitute |
| Docs | Swagger / Swashbuckle |

## How It Works

**Request flow:**

```
HTTP request
  -> Kestrel (web server)
    -> TodosController (routes to the right handler)
      -> MediatR.Send(command/query)
        -> Handler (business logic)
          -> ITodoRepository (data access)
            -> EF Core -> SQLite
```

**CQRS split:**
- **Commands** (write): `CreateTodoCommand`, `UpdateTodoCommand`, `DeleteTodoCommand`
- **Queries** (read): `GetTodosQuery`, `GetTodoByIdQuery`

Each command/query is a `record` with its own `Handler` class. The controller never talks to the database directly.

## API Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/todos` | List all todos (newest first) |
| GET | `/api/todos/{id}` | Get a single todo |
| POST | `/api/todos` | Create a todo |
| PUT | `/api/todos/{id}` | Update a todo |
| DELETE | `/api/todos/{id}` | Delete a todo |

## Running

```bash
cd Source/TodoApi.API
dotnet run
```

Swagger UI: `http://localhost:5279/swagger`

The database (`todo.db`) is created automatically on first startup via EF Core migrations.

## Running Tests

```bash
dotnet test
```

## Key Patterns to Study

- **Primary constructors** (`class Foo(IBar bar)`) — C# 12 shorthand for constructor injection
- **`record` types** — immutable DTOs with value equality, used for commands/queries
- **`sealed` classes** — prevents inheritance, a Skynet convention for handlers/controllers
- **`required` keyword** — compiler-enforced property initialization (`required string Title`)
- **`CancellationToken`** — cooperative cancellation passed through the entire call chain
- **Repository pattern** — interface in Application, implementation in Infrastructure
- **CreatedAtAction** — returns 201 with a `Location` header pointing to the new resource

## Learning Resources

- [C# syntax (W3Schools)](https://www.w3schools.com/cs/index.php)
- [LINQ](https://learn.microsoft.com/en-us/dotnet/csharp/linq/)
- [EF Core](https://learn.microsoft.com/en-us/ef/core/)
- [EF Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [MediatR](https://github.com/jbogard/MediatR)
- [FluentValidation](https://docs.fluentvalidation.net/)
