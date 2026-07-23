# Архитектура проекта

## Общая картина

```
TodoApi.slnx                          <-- solution: знает обо всех проектах
│
├── Source/
│   ├── TodoApi.Domain/               <-- слой 1: сущности (ноль зависимостей)
│   ├── TodoApi.Application/          <-- слой 2: бизнес-логика (зависит от Domain)
│   ├── TodoApi.Infrastructure/       <-- слой 3: база данных (зависит от Application)
│   └── TodoApi.API/                  <-- слой 4: HTTP-точка входа (зависит от всех)
│
└── Tests/
    └── TodoApi.Tests/                <-- тесты (зависит от Application + Domain)
```

## Граф зависимостей

```
API  ──────►  Infrastructure  ──────►  Application  ──────►  Domain
                                           │
                                      Tests ┘
```

Стрелка значит "знает о" / "ссылается на". Зависимости идут только вправо — Domain не знает ни о ком.

---

## Слой 1: Domain

**Папка:** `Source/TodoApi.Domain/`
**Зависимости:** никаких
**Содержит:** сущности (entities) — чистые C#-классы, описывающие объекты предметной области

### TodoItem.cs

```csharp
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

**Что тут происходит:**

- `sealed` — запрещает наследование. Конвенция Skynet: если класс не задумывался как базовый, делай его `sealed`.
- `required string Title` — компилятор не даст создать `TodoItem` без `Title`. Без `required` можно случайно написать `new TodoItem()` и получить `null` в `Title`.
- `string? Description` — знак `?` значит "это поле может быть null". Без `?` компилятор предупредит, если ты попытаешься записать туда `null`.
- `DateTime CreatedAt { get; set; } = DateTime.UtcNow` — значение по умолчанию: если не задать явно, возьмёт текущее UTC-время.
- `DateTime? CompletedAt` — nullable: пока задача не выполнена, тут `null`.

**Почему Domain не зависит ни от чего:**

`TodoItem` не знает, что существует база данных, HTTP, MediatR. Он описывает ТОЛЬКО "что такое задача". Это позволяет:
- Менять базу данных (SQLite → PostgreSQL → SQL Server) без изменений в Domain
- Менять HTTP-фреймворк без изменений в Domain
- Тестировать бизнес-логику без базы данных

---

## Слой 2: Application

**Папка:** `Source/TodoApi.Application/`
**Зависимости:** Domain, MediatR, FluentValidation
**Содержит:** бизнес-логика — commands, queries, handlers, validators, интерфейсы

### ITodoRepository.cs — контракт доступа к данным

```csharp
public interface ITodoRepository
{
    Task<List<TodoItem>> GetAllAsync(CancellationToken ct);
    Task<TodoItem?> GetByIdAsync(int id, CancellationToken ct);
    Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct);
    Task UpdateAsync(TodoItem item, CancellationToken ct);
    Task DeleteAsync(TodoItem item, CancellationToken ct);
}
```

Это **интерфейс** — он говорит "мне нужно уметь создавать, читать, обновлять и удалять задачи", но НЕ говорит КАК. Реализация будет в Infrastructure. Это и есть Dependency Inversion — Application определяет что нужно, Infrastructure предоставляет как.

**`Task<T>`** — асинхронный возврат. Все операции с БД асинхронные, чтобы не блокировать поток сервера пока ждём ответа от базы.

**`CancellationToken ct`** — если клиент закрыл соединение (ушёл со страницы), сервер прекратит выполнять запрос, а не будет работать вхолостую.

### Commands — операции записи

**CQRS** (Command Query Responsibility Segregation) — разделение: команды меняют данные, запросы читают.

#### CreateTodoCommand.cs

```csharp
// Что хотим сделать (данные):
public sealed record CreateTodoCommand(string Title, string? Description) : IRequest<TodoItem>;

// Как делаем (логика):
public sealed class CreateTodoHandler(ITodoRepository repository)
    : IRequestHandler<CreateTodoCommand, TodoItem>
{
    public Task<TodoItem> Handle(CreateTodoCommand request, CancellationToken ct)
    {
        var item = new TodoItem
        {
            Title = request.Title,
            Description = request.Description
        };
        return repository.CreateAsync(item, ct);
    }
}
```

**`record`** — неизменяемый тип данных. После создания `new CreateTodoCommand("Buy milk", null)` нельзя поменять `Title`. Идеально для команд и запросов — они описывают намерение, которое не должно меняться.

**`IRequest<TodoItem>`** — маркер MediatR: "это запрос, который возвращает `TodoItem`".

**`IRequestHandler<TRequest, TResponse>`** — MediatR найдёт этот handler автоматически по типу запроса.

**Primary constructor** `CreateTodoHandler(ITodoRepository repository)` — DI-контейнер сам подставит реализацию `ITodoRepository` при создании handler'а. Эквивалент:

```csharp
// Старый синтаксис (до C# 12):
public sealed class CreateTodoHandler : IRequestHandler<CreateTodoCommand, TodoItem>
{
    private readonly ITodoRepository _repository;

    public CreateTodoHandler(ITodoRepository repository)
    {
        _repository = repository;
    }
}
```

#### UpdateTodoCommand.cs

```csharp
public sealed record UpdateTodoCommand(int Id, string Title, string? Description, bool IsCompleted)
    : IRequest<TodoItem?>;

public sealed class UpdateTodoHandler(ITodoRepository repository)
    : IRequestHandler<UpdateTodoCommand, TodoItem?>
{
    public async Task<TodoItem?> Handle(UpdateTodoCommand request, CancellationToken ct)
    {
        var item = await repository.GetByIdAsync(request.Id, ct);
        if (item is null) return null;           // не нашли — вернём null, контроллер вернёт 404

        item.Title = request.Title;
        item.Description = request.Description;

        if (request.IsCompleted && !item.IsCompleted)
            item.CompletedAt = DateTime.UtcNow;  // только что завершили — ставим дату

        if (!request.IsCompleted)
            item.CompletedAt = null;              // сняли галочку — убираем дату

        item.IsCompleted = request.IsCompleted;

        await repository.UpdateAsync(item, ct);
        return item;
    }
}
```

**`TodoItem?`** (с `?`) — handler может вернуть `null` (если задача не найдена). Контроллер проверит и вернёт 404.

**`is null`** — pattern matching проверка на null (вместо `== null`).

#### DeleteTodoCommand.cs

```csharp
public sealed record DeleteTodoCommand(int Id) : IRequest<bool>;
```

Возвращает `bool`: `true` — удалено, `false` — не нашли (контроллер вернёт 404).

### Queries — операции чтения

#### GetTodosQuery.cs

```csharp
public sealed record GetTodosQuery : IRequest<List<TodoItem>>;

public sealed class GetTodosHandler(ITodoRepository repository)
    : IRequestHandler<GetTodosQuery, List<TodoItem>>
{
    public Task<List<TodoItem>> Handle(GetTodosQuery request, CancellationToken ct)
        => repository.GetAllAsync(ct);
}
```

**Expression-bodied member** (`=>` вместо `{ return ... }`) — сокращённый синтаксис для однострочных методов.

#### GetTodoByIdQuery.cs

```csharp
public sealed record GetTodoByIdQuery(int Id) : IRequest<TodoItem?>;
```

### Validators — правила валидации

#### CreateTodoCommandValidator.cs

```csharp
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

FluentValidation позволяет писать правила декларативно. `RuleFor(x => x.Title)` — "для поля Title применяй такие правила". Каждое правило цепочкой: `.NotEmpty()` + `.MaximumLength(200)`.

---

## Слой 3: Infrastructure

**Папка:** `Source/TodoApi.Infrastructure/`
**Зависимости:** Application (а через неё — Domain), EF Core, SQLite provider
**Содержит:** реализация доступа к данным — DbContext, Repository, Migrations

### TodoDbContext.cs — мост между C# и базой данных

```csharp
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

**`DbContext`** — главный класс EF Core. Он:
1. Управляет соединением с БД
2. Отслеживает изменения в объектах (change tracking)
3. Генерирует SQL из LINQ-запросов
4. Применяет миграции

**`DbSet<TodoItem> Todos`** — "таблица `Todos` содержит строки типа `TodoItem`". Когда пишешь `db.Todos.Where(...)`, EF превращает это в `SELECT ... FROM Todos WHERE ...`.

**`OnModelCreating`** — настройка маппинга: какое свойство → какая колонка, ограничения, ключи. `HasMaxLength(200)` → в SQLite это `TEXT` с ограничением, в SQL Server было бы `nvarchar(200)`.

### TodoRepository.cs — реализация контракта

```csharp
public sealed class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public Task<List<TodoItem>> GetAllAsync(CancellationToken ct)
        => db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public Task<TodoItem?> GetByIdAsync(int id, CancellationToken ct)
        => db.Todos.FindAsync([id], ct).AsTask();

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Add(item);          // пометить как "новый" в change tracker
        await db.SaveChangesAsync(ct); // сгенерировать INSERT и выполнить
        return item;                   // item.Id теперь заполнен базой данных
    }

    public Task UpdateAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Update(item);         // пометить как "изменённый"
        return db.SaveChangesAsync(ct); // сгенерировать UPDATE
    }

    public Task DeleteAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Remove(item);         // пометить как "удалённый"
        return db.SaveChangesAsync(ct); // сгенерировать DELETE
    }
}
```

**`db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync(ct)`** — это LINQ. EF Core превращает C#-выражение `OrderByDescending(t => t.CreatedAt)` в SQL `ORDER BY CreatedAt DESC`. `.ToListAsync()` — выполняет запрос и возвращает результат.

**`FindAsync([id], ct)`** — ищет по primary key. Сначала проверяет change tracker (вдруг объект уже загружен), потом идёт в БД.

**`.AsTask()`** — `FindAsync` возвращает `ValueTask`, а интерфейс ожидает `Task`. `.AsTask()` конвертирует.

### Migrations/ — история изменений схемы

```
Migrations/
  20260720144404_InitialCreate.cs          <-- что менять (Up/Down)
  20260720144404_InitialCreate.Designer.cs <-- снапшот модели на момент миграции
  TodoDbContextModelSnapshot.cs            <-- текущее состояние модели
```

`InitialCreate.cs` содержит два метода:
- `Up()` — создаёт таблицу `Todos` (применяется при `dotnet ef database update`)
- `Down()` — удаляет таблицу (откат миграции)

EF генерирует миграции автоматически, сравнивая текущую C#-модель с последним снапшотом.

---

## Слой 4: API

**Папка:** `Source/TodoApi.API/`
**Зависимости:** Application, Infrastructure (+ Swagger, EF Design)
**Содержит:** HTTP endpoints, DI-конфигурация, запуск приложения

### Controllers/TodosController.cs — HTTP-точка входа

```csharp
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class TodosController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var todos = await mediator.Send(new GetTodosQuery(), ct);
        return Ok(todos);                                    // 200 + JSON
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var todo = await mediator.Send(new GetTodoByIdQuery(id), ct);
        return todo is not null ? Ok(todo) : NotFound();     // 200 или 404
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTodoCommand command, CancellationToken ct)
    {
        var todo = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);  // 201
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTodoCommand command, CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest("Route id must match body id");  // 400

        var todo = await mediator.Send(command, ct);
        return todo is not null ? Ok(todo) : NotFound();       // 200 или 404
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await mediator.Send(new DeleteTodoCommand(id), ct);
        return deleted ? NoContent() : NotFound();             // 204 или 404
    }
}
```

**Атрибуты:**
- `[ApiController]` — включает автоматическую валидацию модели и привязку параметров
- `[Route("api/[controller]")]` — `[controller]` заменяется на имя класса без суффикса: `TodosController` → `api/todos`
- `[HttpGet("{id:int}")]` — `{id:int}` значит: параметр `id` должен быть целым числом. `/api/todos/abc` вернёт 404, а не ошибку.
- `[FromBody]` — десериализовать тело запроса из JSON в объект
- `[Produces("application/json")]` — подсказка для Swagger: все ответы в JSON

**HTTP-коды:**
- `Ok()` → 200
- `CreatedAtAction()` → 201 + заголовок `Location: /api/todos/1`
- `NoContent()` → 204 (удалено, тело ответа пустое)
- `BadRequest()` → 400
- `NotFound()` → 404

**Контроллер тонкий:** ноль бизнес-логики. Его единственная работа — принять HTTP, вызвать MediatR, вернуть HTTP-код.

### Program.cs — точка входа приложения

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Регистрация сервисов (DI-контейнер):
builder.Services.AddControllers();              // контроллеры
builder.Services.AddEndpointsApiExplorer();     // для Swagger
builder.Services.AddSwaggerGen();               // Swagger UI

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite("Data Source=todo.db"));   // EF Core + SQLite

builder.Services.AddScoped<ITodoRepository, TodoRepository>();  // когда кто-то просит
                                                                // ITodoRepository — дай TodoRepository

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<CreateTodoCommand>());  // найди все handlers

builder.Services.AddValidatorsFromAssemblyContaining<CreateTodoCommandValidator>();  // найди все validators

var app = builder.Build();

// 2. Авто-миграция:
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.Migrate();  // создать/обновить таблицы
}

// 3. Middleware pipeline:
app.UseSwagger();       // endpoint /swagger/v1/swagger.json
app.UseSwaggerUI();     // endpoint /swagger — интерактивная документация
app.MapControllers();   // привязать контроллеры к роутам
app.Run();              // запустить Kestrel-сервер
```

**`AddScoped`** — создаёт новый экземпляр сервиса на каждый HTTP-запрос. Один запрос = один `TodoRepository` = один `TodoDbContext` = одна транзакция.

**`RegisterServicesFromAssemblyContaining<T>`** — MediatR сканирует сборку, где лежит `T`, и находит все `IRequestHandler<,>` автоматически.

---

## Слой Tests

**Папка:** `Tests/TodoApi.Tests/`
**Зависимости:** Application, Domain, xUnit, NSubstitute
**Содержит:** unit-тесты handler'ов

```csharp
public sealed class CreateTodoHandlerTests
{
    // Мок репозитория — не настоящая БД, а подставной объект:
    private readonly ITodoRepository _repository = Substitute.For<ITodoRepository>();
    private readonly CreateTodoHandler _handler;

    public CreateTodoHandlerTests()
    {
        // Настройка мока: когда вызовут CreateAsync — верни тот же объект:
        _repository.CreateAsync(Arg.Any<TodoItem>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<TodoItem>());

        _handler = new CreateTodoHandler(_repository);
    }

    [Fact]
    public async Task Handle_SetsTitle()
    {
        var command = new CreateTodoCommand("Buy groceries", null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Buy groceries", result.Title);
    }
}
```

**Тестируем handler напрямую**, не через HTTP. Не нужен веб-сервер, не нужна база данных. Мок (`Substitute.For<ITodoRepository>()`) имитирует репозиторий.

---

## Как данные проходят через все слои

Пример: `POST /api/todos` с телом `{ "title": "Buy milk" }`

```
1. Kestrel принимает HTTP-запрос

2. ASP.NET Core десериализует JSON → CreateTodoCommand("Buy milk", null)

3. TodosController.Create() вызывает mediator.Send(command)

4. MediatR находит CreateTodoHandler по типу команды

5. CreateTodoHandler.Handle():
   - Создаёт new TodoItem { Title = "Buy milk" }
   - Вызывает repository.CreateAsync(item)

6. TodoRepository.CreateAsync():
   - db.Todos.Add(item)      → EF помечает объект как "новый"
   - db.SaveChangesAsync()   → EF генерирует SQL:
     INSERT INTO Todos (Title, Description, IsCompleted, CreatedAt, CompletedAt)
     VALUES ('Buy milk', NULL, 0, '2026-07-20T14:44:04Z', NULL)
   - SQLite записывает на диск, возвращает Id = 1
   - EF заполняет item.Id = 1

7. Handler возвращает item контроллеру

8. Контроллер вызывает CreatedAtAction() → ASP.NET Core:
   - Сериализует item в JSON
   - Ставит HTTP-код 201
   - Добавляет заголовок Location: /api/todos/1
   - Отправляет ответ клиенту
```

---

## Файлы конфигурации

### TodoApi.slnx — solution file

```xml
<Solution>
  <Folder Name="/Source/">
    <Project Path="Source/TodoApi.API/TodoApi.API.csproj" />
    <Project Path="Source/TodoApi.Application/TodoApi.Application.csproj" />
    <Project Path="Source/TodoApi.Domain/TodoApi.Domain.csproj" />
    <Project Path="Source/TodoApi.Infrastructure/TodoApi.Infrastructure.csproj" />
  </Folder>
  <Folder Name="/Tests/">
    <Project Path="Tests/TodoApi.Tests/TodoApi.Tests.csproj" />
  </Folder>
</Solution>
```

Группирует все `.csproj` проекты. `dotnet build` из корня соберёт всё.

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=todo.db"
  }
}
```

`Data Source=todo.db` — путь к файлу SQLite. В production здесь была бы строка подключения к SQL Server.

### launchSettings.json

```json
{
  "profiles": {
    "http": {
      "applicationUrl": "http://localhost:5279",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Порт, на котором запускается сервер при `dotnet run`. `Development` — режим разработки (подробные ошибки, Swagger UI).
