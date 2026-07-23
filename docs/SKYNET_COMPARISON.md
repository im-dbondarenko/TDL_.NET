# Todo API vs Skynet Production Codebase

Пофайловое сравнение учебного проекта с реальным production кодом Skynet.Loans — основного микросервиса платформы LenderCom (управление ипотечными займами, ~100+ таблиц, ~40+ репозиториев).

---

## 1. Domain Entity

### Todo API — `Source/TodoApi.Domain/TodoItem.cs`

```csharp
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans.Repository.MSSql/Entities/LoanRequests/LoanRequest.cs`

```csharp
internal class LoanRequest : ITenantEntityV2
{
    public LoanRequestId Id { get; set; } = null!;
    public CompanyId CompanyId { get; set; }
    public ApplicationId ApplicationId { get; set; }
    public LoanRequestType RequestType { get; set; } = null!;
    public LoanRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public LoanRequestContent RequestContent { get; set; } = new() { Kind = BlobUploadStateKind.Skipped };
    public Metadata? Metadata { get; set; }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Видимость | `public` | `internal` — entity не выходит за пределы Infrastructure слоя |
| Типы ID | `Guid Id` | `LoanRequestId Id` — strongly-typed ID обертки (типобезопасность: нельзя случайно передать `CompanyId` вместо `ApplicationId`). Todo API использует `Guid` — ближе к Skynet, чем `int` |
| Multi-tenancy | нет | `ITenantEntityV2` + `CompanyId` — каждая строка привязана к компании-арендатору |
| Конфигурация | Fluent API в `OnModelCreating` | Отдельный класс `IEntityTypeConfiguration<LoanRequest>` — entity ничего не знает о БД |
| JSON-колонки | нет | `LoanRequestContent` — owned type, сериализуется в JSON-колонку (`HasConversion`) |

---

## 2. Controller

### Todo API — `Source/TodoApi.API/Controllers/TodosController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class TodosController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var todo = await mediator.Send(new GetTodoByIdQuery(id), ct);
        return todo is not null ? Ok(todo) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTodoCommand command, CancellationToken ct)
    {
        var todo = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);
    }
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans.API/Controllers/TagsController.cs`

```csharp
[ApiController]
[Route("api/companies/{companyId}/tags")]
public sealed class TagsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TagsController(IMediator mediator) { _mediator = mediator; }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetTags(
        [FromRoute] Guid companyId,
        [FromQuery] bool? includeGlobal,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTagsQuery(...), cancellationToken);
        return Ok(result);
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Роутинг | `api/todos` | `api/companies/{companyId}/tags` — все роуты содержат `companyId` (multi-tenancy) |
| Авторизация | нет (учебный) | `[Authorize]` на каждом endpoint + глобальный JWT middleware |
| DI-стиль | Primary constructor | Классический конструктор (Skynet.Loans был написан до C# 12) |
| Swagger | базовый | `[SwaggerResponseExample]` / `[SwaggerRequestExample]` — кастомные примеры в Swagger UI |
| Контроллер делает | диспатч + маппинг HTTP-кодов | ТОЛЬКО диспатч — ноль логики, тонкий passthrough к MediatR |

---

## 3. MediatR Command + Handler

### Todo API — `Source/TodoApi.Application/Todos/CreateTodoCommand.cs`

```csharp
public sealed record CreateTodoCommand(string Title, string? Description) : IRequest<TodoItem>;

public sealed class CreateTodoHandler(ITodoRepository repository)
    : IRequestHandler<CreateTodoCommand, TodoItem>
{
    public Task<TodoItem> Handle(CreateTodoCommand request, CancellationToken ct)
    {
        var item = new TodoItem { Title = request.Title, Description = request.Description };
        return repository.CreateAsync(item, ct);
    }
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans/LoanExceptions/CreateLoanExceptionCommandHandler.cs`

```csharp
// Command (в отдельной Abstractions-сборке):
public sealed record CreateLoanExceptionCommand(CreateLoanExceptionArgs Args) : IRequest<LoanException>;

// Handler (в Application-сборке):
internal sealed class CreateLoanExceptionCommandHandler(
    AuthorizationContext authorizationContext,
    ILoanExceptionsRepository repository,
    TimeProvider timeProvider)
    : IRequestHandler<CreateLoanExceptionCommand, LoanException>
{
    public Task<LoanException> Handle(CreateLoanExceptionCommand request, CancellationToken ct)
    {
        Validate(request.Args);
        return repository.Create(
            authorizationContext.CompanyId,
            request.Args,
            authorizationContext.UserId,
            timeProvider.GetUtcNow().UtcDateTime, ct);
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Command и Handler | В одном файле | Command в `Domain/Abstractions/UseCases/`, Handler в `Application/` — физическое разделение по слоям |
| Видимость Handler | `public` | `internal sealed` — handler не виден извне Application-сборки |
| AuthorizationContext | нет | Инжектится в handler — несет `CompanyId`, `UserId`, permissions (резолвится из JWT middleware) |
| Время | `DateTime.UtcNow` | `TimeProvider` — абстракция для тестируемости (можно мокать время в тестах) |
| Входные данные | Плоские поля (`string Title`) | Обернуты в DTO/Args-объект (`CreateLoanExceptionArgs`) |

---

## 4. MediatR Query + Handler

### Todo API — `Source/TodoApi.Application/Todos/GetTodoByIdQuery.cs`

```csharp
public sealed record GetTodoByIdQuery(Guid Id) : IRequest<TodoItem?>;

public sealed class GetTodoByIdHandler(ITodoRepository repository)
    : IRequestHandler<GetTodoByIdQuery, TodoItem?>
{
    public Task<TodoItem?> Handle(GetTodoByIdQuery request, CancellationToken ct)
        => repository.GetByIdAsync(request.Id, ct);
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans/LoanRequests/GetLoanRequestByIdQueryHandler.cs`

```csharp
internal sealed class GetLoanRequestByIdQueryHandler(
    ILoanRequestRepository repository,
    AuthorizationContext authorizationContext)
    : IRequestHandler<GetLoanRequestByIdQuery, LoanRequestModel?>
{
    public Task<LoanRequestModel?> Handle(GetLoanRequestByIdQuery request, CancellationToken ct)
        => repository.GetOrDefault(authorizationContext.CompanyId, request.Id, ct);
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Tenant scoping | `repository.GetByIdAsync(id)` | `repository.GetOrDefault(companyId, id)` — ВСЕГДА передается `CompanyId`, запрос никогда не сделает select без фильтра по компании |
| Возвращаемый тип | `TodoItem?` (та же entity) | `LoanRequestModel?` — доменная модель, не EF entity (маппинг entity→model внутри репозитория) |
| Имя метода | `GetByIdAsync` | `GetOrDefault` — явно говорит: вернет null, не бросит exception |

---

## 5. DbContext

### Todo API — `Source/TodoApi.Infrastructure/TodoDbContext.cs`

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

### Skynet.Loans — `Interfirst.Skynet.Loans.Repository.MSSql/LoansDbContext.cs`

```csharp
internal class LoansDbContext : DbContext
{
    // ~100+ DbSets:
    public DbSet<LoanPipeline> LoanPipelines { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<Checklist> Checklists { get; set; }
    public DbSet<MilestoneDefinition> MilestoneDefinitions { get; set; }
    // ...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("loans");
        modelBuilder.ApplyConfiguration(new LoanPipelineConfiguration())
                    .ApplyConfiguration(new TagConfiguration())
                    // ... 40+ ApplyConfiguration calls
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Provider | SQLite (файл `todo.db`) | SQL Server (Azure SQL) |
| Schema | default (`dbo`) | Именованная схема `loans` — каждый микросервис имеет свою схему на общей БД |
| Конфигурация | Inline в `OnModelCreating` | Вынесена в отдельные классы `IEntityTypeConfiguration<T>` — `OnModelCreating` только вызывает `ApplyConfiguration()` |
| Регистрация | `AddDbContext<T>()` | `AddPooledDbContextFactory<T>()` — пул контекстов для производительности |
| Видимость | `public` | `internal` — контекст не доступен извне Infrastructure-сборки |
| Объем | 1 DbSet | ~100+ DbSets |
| Value converters | нет | `ConfigureConventions` — глобальная конвертация strongly-typed ID в Guid/int |

---

## 6. Repository

### Todo API — `Source/TodoApi.Application/ITodoRepository.cs` + `Source/TodoApi.Infrastructure/TodoRepository.cs`

```csharp
// Interface:
public interface ITodoRepository
{
    Task<List<TodoItem>> GetAllAsync(CancellationToken ct);
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct);
    Task UpdateAsync(TodoItem item, CancellationToken ct);
    Task DeleteAsync(TodoItem item, CancellationToken ct);
}

// Implementation:
public sealed class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public Task<List<TodoItem>> GetAllAsync(CancellationToken ct)
        => db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans.Repository.Interfaces/IPurchaseAdviceRepository.cs` + `.../Repository/PurchaseAdviceRepository.cs`

```csharp
// Interface (в Application-слое):
public interface IPurchaseAdviceRepository
{
    Task<PurchaseAdviceModel> Create(PurchaseAdviceModel record, CancellationToken ct);
    Task<PurchaseAdviceModel> Update(PurchaseAdviceModel record, CancellationToken ct);
    Task<PurchaseAdviceModel?> Get(PurchaseAdviceId id, CompanyId companyId, CancellationToken ct);
    Task Delete(PurchaseAdviceId id, CompanyId companyId, CancellationToken ct);
}

// Implementation (в Infrastructure-слое):
internal sealed class PurchaseAdviceRepository(LoansDbContext db) : IPurchaseAdviceRepository
{
    public async Task<PurchaseAdviceModel?> Get(PurchaseAdviceId id, CompanyId companyId, CancellationToken ct)
    {
        var entity = await db.PurchaseAdvices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
        return entity?.ToModel();
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Базовый класс | нет (и в Skynet тоже нет!) | Нет generic `Repository<T>` — каждый репозиторий уникален с доменно-специфичными методами |
| Entity vs Model | Работает напрямую с entity | Interface принимает/возвращает domain models (`PurchaseAdviceModel`), маппинг entity<->model скрыт внутри implementation (`ToEntity()` / `ToModel()`) |
| Tracking | По умолчанию (EF tracking) | `AsNoTracking()` на read-запросах — оптимизация памяти |
| Multi-tenancy | нет | `CompanyId` — ОБЯЗАТЕЛЬНЫЙ параметр в каждом методе |
| Видимость implementation | `public` | `internal sealed` |

---

## 7. FluentValidation

### Todo API — `Source/TodoApi.Application/Todos/`

```csharp
// TodoValidationRules.cs — общие правила (extension methods):
public static partial class TodoValidationRules
{
    [GeneratedRegex(@"^[a-zA-Z0-9\s\p{P}\p{S}]*$")]
    private static partial Regex LatinOnlyRegex();

    public static IRuleBuilderOptions<T, string> ValidTitle<T>(this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must be 200 characters or fewer")
            .Matches(LatinOnlyRegex()).WithMessage("Title must contain only Latin characters");
    }
}

// CreateTodoCommandValidator.cs — переиспользует ValidTitle() / ValidDescription():
public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
    }
}
```

### Skynet.Loans — `Interfirst.Skynet.Loans/Trades/CreateTradeCommandHandler.cs`

```csharp
// Validator живет ВНУТРИ файла handler'а:
internal sealed class CreateTradeDtoValidator : AbstractValidator<CreateTradeDto>
{
    public CreateTradeDtoValidator()
    {
        RuleFor(x => x.CommitmentAmount).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TradeDate).NotEmpty();
        RuleFor(x => x.ExpirationDate)
            .GreaterThan(x => x.TradeDate)
            .When(x => x.ExpirationDate is not null);
        RuleFor(x => x.MbsPrice)
            .Must(v => v!.Value * 16 % 1 == 0)
            .When(x => x.MbsPrice is not null)
            .WithMessage("MbsPrice must be in 1/16th increments");
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Файл | Общие правила в `TodoValidationRules.cs` (extension methods), отдельный `*Validator.cs` на каждую command | Внутри файла handler'а — validator и handler рядом |
| Регистрация | Через DI (`AddValidatorsFromAssembly`) + `ValidationBehavior` pipeline | `private static readonly` поле на handler'е — вызывается явно: `Validator.ValidateAndThrow(dto)` |
| Pipeline | `ValidationBehavior<TRequest, TResponse>` — MediatR `IPipelineBehavior`, автоматически запускает валидацию ДО handler'а | `IPipelineBehavior` для авторизации; валидация — вручную в handler'е |
| Переиспользование | Extension methods (`ValidTitle()`, `ValidDescription()`) — одно место правды для Create и Update | Каждый validator самостоятельный |
| Сложность правил | `NotEmpty`, `MaximumLength`, `Matches(regex)` для Latin-only | Условные (`.When()`), кросс-поля (`.GreaterThan(x => x.TradeDate)`), доменные проверки (`1/16th increments`), вложенные (`RuleForEach` + `ChildRules`) |

---

## 8. Program.cs / DI Registration

### Todo API — `Source/TodoApi.API/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseSqlite("Data Source=todo.db"));
builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateTodoCommand>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<CreateTodoCommandValidator>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.Migrate();
}
app.UseCors();
app.UseExceptionHandler(...);  // ValidationException → 400 JSON
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();
```

### Skynet.Loans — `Interfirst.Skynet.Loans.API/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// Каждый слой регистрирует свои сервисы через extension method:
services.AddSkynetAuth().AddLoansPolicies();         // auth + policies
services.AddLoansServices(configuration);              // MediatR + handlers
services.AddLoansRepositoryServices(connectionString); // DbContext + repos
services.AddDynamicFieldsSchema();
services.AddLoansServiceBus(configuration);            // Azure Service Bus
```

DI разнесен по extension methods в каждом слое:

```csharp
// Application/ServiceCollectionExtensions.cs:
services.AddMediatR(typeof(ServiceCollectionExtensions).Assembly, ...);
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>));

// Infrastructure/RepositoryServiceCollectionExtensions.cs:
services.AddPooledDbContextFactory<LoansDbContext>(options => options.UseSqlServer(...));
services.AddScoped<ITagsRepository, TagsRepository>();
services.AddScoped<ITasksRepository, TasksRepository>();
// ... 40+ AddScoped вызовов
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Весь DI | В одном `Program.cs` (~15 строк) | Разнесен по `ServiceCollectionExtensions` в каждом слое — `Program.cs` только вызывает `.AddLoansServices()`, `.AddLoansRepositoryServices()` |
| DbContext | `AddDbContext<T>()` | `AddPooledDbContextFactory<T>()` + ручной scoped resolve |
| Миграции | Auto-migrate на старте | Миграции НЕ запускаются из кода — только через CI/CD pipeline |
| Конфигурация | Hardcoded connection string | Azure Key Vault + `IConfiguration` |
| Middleware | CORS, ExceptionHandler (ValidationException → 400), Swagger | Auth, CORS, rate limiting, health checks, ServiceBus, OpenTelemetry |
| Репозитории | 1 `AddScoped` | 40+ отдельных `AddScoped` — никакого auto-scanning |

---

## 9. Unit Tests

### Todo API — `Tests/TodoApi.Tests/CreateTodoHandlerTests.cs`

```csharp
public sealed class CreateTodoHandlerTests
{
    private readonly ITodoRepository _repository = Substitute.For<ITodoRepository>();
    private readonly CreateTodoHandler _handler;

    public CreateTodoHandlerTests()
    {
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

### Skynet.Loans — `Interfirst.Skynet.Loans.Unit.Tests/SubCompanies/GetSubCompanyActiveLoanCountQueryHandlerTests.cs`

```csharp
public class GetSubCompanyActiveLoanCountQueryHandlerTests
{
    private readonly ILoanApplicationRepository _repository =
        Substitute.For<ILoanApplicationRepository>();
    private readonly CompanyId _companyId = CompanyId.NewId();

    private GetSubCompanyActiveLoanCountQueryHandler CreateHandler(AuthorizationContext auth)
        => new(_repository, auth);

    [Fact]
    public async Task Employee_ReturnsRepositoryCount_ForAnySubCompany()
    {
        var subCompanyId = new SubCompanyId(Guid.NewGuid());
        _repository.CountActiveLoansForSubCompany(_companyId, subCompanyId, Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await CreateHandler(CreateEmployeeAuthContext())
            .Handle(new GetSubCompanyActiveLoanCountQuery(subCompanyId), CancellationToken.None);

        Assert.Equal(7, result.Count);
        await _repository.Received(1).CountActiveLoansForSubCompany(
            _companyId, subCompanyId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopedBroker_SubCompanyOutsideSubtree_ThrowsForbidden()
    {
        // Тест на авторизацию: брокер не должен видеть чужие данные
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler(CreateBrokerAuthContext(otherSubCompanyId))
                .Handle(new GetSubCompanyActiveLoanCountQuery(targetSubCompanyId), ...));

        await _repository.DidNotReceive().CountActiveLoansForSubCompany(...);
    }
}
```

### Что отличается

| Аспект | Todo API | Skynet |
|---|---|---|
| Mocking framework | NSubstitute (одинаково!) | NSubstitute |
| Test framework | xUnit (одинаково!) | xUnit |
| Handler creation | В конструкторе теста | Factory method `CreateHandler(auth)` — позволяет создавать handler с разными auth-контекстами |
| Auth-тесты | нет | ОБЯЗАТЕЛЬНО: тесты на `ForbiddenException`, проверка что репозиторий НЕ вызывался (`DidNotReceive`) при отказе доступа |
| Именование | `Handle_SetsTitle` | `Role_Behavior_Condition` (напр. `Employee_ReturnsCount_ForAnySubCompany`) |
| Assertions | `Assert.Equal` (одинаково!) | `Assert.Equal` + `Assert.ThrowsAsync` + `Received`/`DidNotReceive` |

---

## Сводка: что общего, что только в production

### Общее (используем одинаково)

- Clean Architecture (Domain -> Application -> Infrastructure -> API)
- MediatR CQRS (Command/Query + Handler)
- `sealed record` для commands/queries
- `sealed class` для handlers и контроллеров
- Repository pattern (interface в Application, implementation в Infrastructure)
- Controller как тонкий passthrough к MediatR
- EF Core + Fluent API
- FluentValidation + `ValidationBehavior` MediatR pipeline (валидация ДО handler'а)
- Guid-based IDs (безопасность + распределённая генерация)
- Переиспользование правил через extension methods
- xUnit + NSubstitute
- `CancellationToken` everywhere

### Что добавляет production

- **Multi-tenancy** — `CompanyId` и `AuthorizationContext` пронизывают всё
- **Strongly-typed IDs** — `LoanRequestId` вместо голого `Guid` (типобезопасность: нельзя перепутать `CompanyId` с `ApplicationId`)
- **Entity/Model separation** — репозиторий маппит entity <-> domain model
- **`internal` visibility** — entities, handlers, repos скрыты от внешних слоёв
- **Pooled DbContext** — `AddPooledDbContextFactory` для производительности
- **Именованные DB-схемы** — `loans`, `accounts`, `documents` на общей БД
- **Auth-тесты** — каждый handler тестируется на запрет доступа
- **`TimeProvider`** — вместо `DateTime.UtcNow` для тестируемости
- **Pipeline behaviors** — кросс-cutting concerns (авторизация, логирование) через MediatR pipeline
- **Миграции через CI/CD** — никогда из кода, всегда через отдельный PR
- **Azure infrastructure** — Key Vault, Service Bus, SQL Server, App Insights
