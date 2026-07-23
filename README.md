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

**CQRS split** (all in `TodoApi.Application/Todos/` — grouped by domain entity, Skynet convention):
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

## Database

Uses **EF Core** (Entity Framework Core) with **SQLite** as the provider. The database is a single file `todo.db` created automatically on first startup.

### Schema

```
Table: Todos
  Id           INTEGER  PK, auto-increment
  Title        TEXT     NOT NULL, max 200
  Description  TEXT     nullable, max 1000
  IsCompleted  INTEGER  (bool)
  CreatedAt    TEXT     (DateTime UTC)
  CompletedAt  TEXT     (DateTime UTC, nullable)
```

### How EF Core is wired

1. **Entity** (`TodoItem.cs`) — a plain C# class. EF maps each property to a column.

2. **DbContext** (`TodoDbContext.cs`) — the bridge between C# and the database:
   ```csharp
   public DbSet<TodoItem> Todos => Set<TodoItem>();
   ```
   `DbSet<TodoItem>` represents the `Todos` table. LINQ queries against it get translated to SQL.

3. **Model configuration** (`OnModelCreating`) — constraints that don't fit in the entity:
   ```csharp
   entity.HasKey(e => e.Id);
   entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
   ```

4. **Repository** (`TodoRepository.cs`) — wraps `DbContext` calls:
   ```csharp
   public Task<List<TodoItem>> GetAllAsync(CancellationToken ct)
       => db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
   ```
   `db.Todos` is an `IQueryable` — EF translates the `.OrderByDescending().ToListAsync()` chain into `SELECT * FROM Todos ORDER BY CreatedAt DESC`.

5. **Migrations** — EF compares the C# model to the database and generates migration files:
   ```bash
   dotnet ef migrations add <Name>   # generates a migration
   dotnet ef database update          # applies it
   ```
   In this project, `db.Database.Migrate()` in `Program.cs` auto-applies pending migrations on startup.

6. **DI registration** (`Program.cs`):
   ```csharp
   builder.Services.AddDbContext<TodoDbContext>(options =>
       options.UseSqlite("Data Source=todo.db"));
   builder.Services.AddScoped<ITodoRepository, TodoRepository>();
   ```
   `AddDbContext` registers the context with a **scoped** lifetime (one instance per HTTP request). `AddScoped<ITodoRepository, TodoRepository>()` tells the DI container: when someone asks for `ITodoRepository`, give them a `TodoRepository`.

### Data flow for a POST /api/todos

```
Controller receives CreateTodoCommand { Title, Description }
  -> MediatR dispatches to CreateTodoHandler
    -> Handler creates a TodoItem object
    -> Calls repository.CreateAsync(item)
      -> Repository does db.Todos.Add(item)
      -> Repository does db.SaveChangesAsync()
        -> EF generates: INSERT INTO Todos (Title, Description, IsCompleted, CreatedAt) VALUES (...)
        -> SQLite writes to todo.db
        -> EF populates item.Id with the auto-generated value
    -> Handler returns the item (now with Id set)
  -> Controller returns 201 Created with the item as JSON
```

## Key Patterns to Study

- **Primary constructors** (`class Foo(IBar bar)`) — C# 12 shorthand for constructor injection
- **`record` types** — immutable DTOs with value equality, used for commands/queries
- **`sealed` classes** — prevents inheritance, a Skynet convention for handlers/controllers
- **`required` keyword** — compiler-enforced property initialization (`required string Title`)
- **`CancellationToken`** — cooperative cancellation passed through the entire call chain
- **Repository pattern** — interface in Application, implementation in Infrastructure
- **CreatedAtAction** — returns 201 with a `Location` header pointing to the new resource
