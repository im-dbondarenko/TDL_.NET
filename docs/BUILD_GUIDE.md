# Пошаговое руководство: как собрать этот проект с нуля

Последовательность, в которой разработчик создавал бы этот проект вручную, с объяснением каждого шага.

**Предусловие:** установлен .NET SDK 10+ (`dotnet --version`).

---

## Шаг 1. Создать структуру папок и solution

```bash
mkdir TodoApi && cd TodoApi
mkdir -p Source Tests
```

Создать solution file — корневой файл, который объединяет все проекты:

```bash
dotnet new sln -n TodoApi
```

Появится `TodoApi.sln` (или `.slnx` в .NET 10).

---

## Шаг 2. Создать проект Domain (слой сущностей)

**Почему первым:** Domain — самый внутренний слой, от него зависят все остальные. Нет Domain → нечего использовать в Application.

```bash
dotnet new classlib -n TodoApi.Domain -o Source/TodoApi.Domain
dotnet sln add Source/TodoApi.Domain
```

`classlib` — шаблон "библиотека классов" (не веб, не консольное). Создаёт `.csproj` + пустой `Class1.cs`.

Удалить `Class1.cs` и создать сущность:

```bash
rm Source/TodoApi.Domain/Class1.cs
```

Написать `Source/TodoApi.Domain/TodoItem.cs`:

```csharp
namespace TodoApi.Domain;

public sealed class TodoItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

Проверить что компилируется:

```bash
dotnet build Source/TodoApi.Domain
```

---

## Шаг 3. Создать проект Application (бизнес-логика)

**Почему вторым:** Application зависит от Domain (использует `TodoItem`), но не зависит от базы данных.

```bash
dotnet new classlib -n TodoApi.Application -o Source/TodoApi.Application
dotnet sln add Source/TodoApi.Application
rm Source/TodoApi.Application/Class1.cs
```

Добавить ссылку на Domain:

```bash
dotnet add Source/TodoApi.Application reference Source/TodoApi.Domain
```

Добавить NuGet-пакеты:

```bash
dotnet add Source/TodoApi.Application package MediatR
dotnet add Source/TodoApi.Application package FluentValidation.DependencyInjectionExtensions
```

Теперь писать код. Порядок файлов внутри Application:

### 3.1. Интерфейс репозитория

`Source/TodoApi.Application/ITodoRepository.cs`:

```csharp
using TodoApi.Domain;

namespace TodoApi.Application;

public interface ITodoRepository
{
    Task<List<TodoItem>> GetAllAsync(CancellationToken ct);
    Task<TodoItem?> GetByIdAsync(int id, CancellationToken ct);
    Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct);
    Task UpdateAsync(TodoItem item, CancellationToken ct);
    Task DeleteAsync(TodoItem item, CancellationToken ct);
}
```

**Почему сначала интерфейс:** handler'ы будут зависеть от `ITodoRepository`. Реализация будет позже в Infrastructure, но handler'ам она не нужна — им достаточно контракта.

### 3.2. Queries и Commands

Создать папку `Todos/` — все commands, queries и validators для одной сущности живут рядом (группировка по домену, конвенция Skynet):

`Source/TodoApi.Application/Todos/GetTodosQuery.cs`:

```csharp
using MediatR;
using TodoApi.Domain;

namespace TodoApi.Application.Todos;

public sealed record GetTodosQuery : IRequest<List<TodoItem>>;

public sealed class GetTodosHandler(ITodoRepository repository)
    : IRequestHandler<GetTodosQuery, List<TodoItem>>
{
    public Task<List<TodoItem>> Handle(GetTodosQuery request, CancellationToken ct)
        => repository.GetAllAsync(ct);
}
```

`Source/TodoApi.Application/Todos/GetTodoByIdQuery.cs` — аналогично, с `int Id` параметром.

Затем три command-файла в той же папке `Todos/`:

- `CreateTodoCommand.cs` — создание задачи
- `UpdateTodoCommand.cs` — обновление (с логикой `CompletedAt`)
- `DeleteTodoCommand.cs` — удаление

### 3.3. Validator

`Source/TodoApi.Application/Todos/CreateTodoCommandValidator.cs`:

```csharp
using FluentValidation;

namespace TodoApi.Application.Todos;

public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must be 200 characters or fewer");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer");
    }
}
```

Проверить компиляцию:

```bash
dotnet build Source/TodoApi.Application
```

---

## Шаг 4. Создать проект Infrastructure (база данных)

**Почему третьим:** Infrastructure реализует `ITodoRepository` из Application. Без интерфейса реализовывать нечего.

```bash
dotnet new classlib -n TodoApi.Infrastructure -o Source/TodoApi.Infrastructure
dotnet sln add Source/TodoApi.Infrastructure
rm Source/TodoApi.Infrastructure/Class1.cs
```

Добавить ссылку на Application (а через неё автоматически подтянется Domain):

```bash
dotnet add Source/TodoApi.Infrastructure reference Source/TodoApi.Application
```

Добавить NuGet-пакеты:

```bash
dotnet add Source/TodoApi.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add Source/TodoApi.Infrastructure package Microsoft.EntityFrameworkCore.Design
```

### 4.1. DbContext

`Source/TodoApi.Infrastructure/TodoDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TodoApi.Domain;

namespace TodoApi.Infrastructure;

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
        });
    }
}
```

### 4.2. Repository

`Source/TodoApi.Infrastructure/TodoRepository.cs` — реализация `ITodoRepository` через `TodoDbContext`.

Проверить компиляцию:

```bash
dotnet build Source/TodoApi.Infrastructure
```

---

## Шаг 5. Создать проект API (HTTP-точка входа)

**Почему четвёртым:** API зависит от всех слоёв и связывает их через DI. Без Application (handlers) и Infrastructure (repos) контроллеру нечего вызывать.

```bash
dotnet new webapi -n TodoApi.API -o Source/TodoApi.API --no-openapi
dotnet sln add Source/TodoApi.API
```

`webapi` — шаблон ASP.NET Core Web API. `--no-openapi` — не генерировать минимальный OpenAPI (мы поставим Swagger сами).

Добавить ссылки на Application и Infrastructure:

```bash
dotnet add Source/TodoApi.API reference Source/TodoApi.Application
dotnet add Source/TodoApi.API reference Source/TodoApi.Infrastructure
```

Добавить NuGet-пакеты:

```bash
dotnet add Source/TodoApi.API package Swashbuckle.AspNetCore
dotnet add Source/TodoApi.API package Microsoft.EntityFrameworkCore.Design
```

### 5.1. Удалить шаблонный код

Шаблон `webapi` создаёт `WeatherForecast.cs` и минимальный `Program.cs` с minimal API. Удалить всё шаблонное:

```bash
rm Source/TodoApi.API/TodoApi.API.http 2>/dev/null  # HTTP-файл
```

### 5.2. Написать Program.cs

Зарегистрировать все сервисы (DbContext, Repository, MediatR, Validators) + настроить middleware (Swagger, маппинг контроллеров).

### 5.3. Написать контроллер

`Source/TodoApi.API/Controllers/TodosController.cs` — 5 endpoints (GET all, GET by id, POST, PUT, DELETE).

### 5.4. Настроить appsettings.json

Добавить connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=todo.db"
  }
}
```

Проверить что проект компилируется:

```bash
dotnet build Source/TodoApi.API
```

---

## Шаг 6. Создать EF-миграцию

**Почему после API:** команда `dotnet ef` требует startup project (API) чтобы знать, как настроен `DbContext` (connection string, provider).

Установить EF CLI tool (один раз):

```bash
dotnet tool install --global dotnet-ef
```

Создать миграцию:

```bash
dotnet ef migrations add InitialCreate \
  --project Source/TodoApi.Infrastructure \
  --startup-project Source/TodoApi.API
```

- `--project` — где лежит `DbContext` (Infrastructure)
- `--startup-project` — где лежит `Program.cs` с конфигурацией (API)

Появятся файлы в `Source/TodoApi.Infrastructure/Migrations/`.

---

## Шаг 7. Первый запуск

```bash
cd Source/TodoApi.API
dotnet run
```

При старте `db.Database.Migrate()` в `Program.cs` применит миграцию и создаст файл `todo.db`.

Открыть в браузере: `http://localhost:5279/swagger`

Проверить через curl:

```bash
# Создать задачу:
curl -X POST http://localhost:5279/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy milk", "description": "2% fat"}'

# Получить все задачи:
curl http://localhost:5279/api/todos

# Обновить задачу (пометить выполненной):
curl -X PUT http://localhost:5279/api/todos/1 \
  -H "Content-Type: application/json" \
  -d '{"id": 1, "title": "Buy milk", "description": "2% fat", "isCompleted": true}'

# Удалить задачу:
curl -X DELETE http://localhost:5279/api/todos/1
```

---

## Шаг 8. Написать тесты

**Почему после запуска:** сначала убедись, что приложение работает руками. Потом автоматизируй проверки.

```bash
dotnet new xunit -n TodoApi.Tests -o Tests/TodoApi.Tests
dotnet sln add Tests/TodoApi.Tests
```

Добавить ссылки и пакеты:

```bash
dotnet add Tests/TodoApi.Tests reference Source/TodoApi.Application
dotnet add Tests/TodoApi.Tests reference Source/TodoApi.Domain
dotnet add Tests/TodoApi.Tests reference Source/TodoApi.Infrastructure
dotnet add Tests/TodoApi.Tests package NSubstitute
```

Написать тесты для handler'ов (мокая репозиторий через NSubstitute).

Запустить:

```bash
dotnet test
```

---

## Шаг 9. Git

```bash
cd TodoApi  # корень проекта
git init

# .gitignore для .NET (игнорирует bin/, obj/, *.db и т.д.):
dotnet new gitignore

git add .
git commit -m "feat: Todo API with Clean Architecture + MediatR + EF Core"
```

---

## Итого: порядок и почему именно такой

```
Шаг 1.  Solution + папки           ← каркас
Шаг 2.  Domain (entity)            ← сначала то, от чего все зависят
Шаг 3.  Application (handlers)     ← бизнес-логика, зависит от Domain
Шаг 4.  Infrastructure (EF + repo) ← реализация доступа к данным
Шаг 5.  API (controller + DI)      ← точка входа, связывает всё
Шаг 6.  Миграция                   ← требует и Infrastructure, и API
Шаг 7.  Запуск + ручная проверка   ← убедиться что работает
Шаг 8.  Тесты                      ← автоматизировать проверки
Шаг 9.  Git                        ← зафиксировать результат
```

Принцип: **сначала внутренние слои, потом внешние**. Каждый следующий шаг зависит от предыдущего — если поменять порядок, `dotnet build` будет падать из-за отсутствующих зависимостей.

---

## Что менять при создании своего проекта

| Что заменить | Где | На что |
|---|---|---|
| `TodoItem` | Domain | Своя сущность (`Product`, `Order`, `User`) |
| `ITodoRepository` | Application | Свой интерфейс с нужными методами |
| `CreateTodoCommand` | Application/Todos | Своя команда с нужными полями |
| `TodoDbContext` + `DbSet<TodoItem>` | Infrastructure | Свой контекст + свои DbSet'ы |
| `TodoRepository` | Infrastructure | Своя реализация |
| `TodosController` | API/Controllers | Свой контроллер с нужными endpoints |
| `todo.db` / SQLite | appsettings.json | Другой provider (PostgreSQL, SQL Server) |

Структура, паттерны и конвенции остаются теми же.
