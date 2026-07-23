# Как устроена валидация

Вся валидация — серверная. UI не проверяет ничего: отправляет данные как есть и показывает ошибки из ответа сервера.

---

## Общая схема

```
UI (fetch POST/PUT)
  ↓
Controller (принимает JSON)
  ↓
MediatR.Send(command)
  ↓
ValidationBehavior<TRequest, TResponse>     ← перехватывает ДО handler'а
  ↓ ищет все IValidator<TRequest>
  ↓ запускает ValidateAsync()
  ↓ если есть ошибки → throw ValidationException
  ↓ если всё ок → next() (передаёт handler'у)
  ↓
Handler (бизнес-логика)
  ↓
Ответ клиенту (200/201)

--- если ValidationException ---

ExceptionHandler middleware
  ↓ ловит ValidationException
  ↓ группирует ошибки по PropertyName
  ↓ возвращает HTTP 400 + JSON { errors: { "Title": ["..."], "Description": ["..."] } }
  ↓
UI (showErrors → рендерит список ошибок)
```

---

## Компоненты

### 1. Shared validation rules (`TodoValidationRules.cs`)

Правила вынесены в extension methods, чтобы не дублировать между `CreateTodoCommandValidator` и `UpdateTodoCommandValidator`.

```csharp
public static partial class TodoValidationRules
{
    [GeneratedRegex(@"^[a-zA-Z0-9\s\p{P}\p{S}]*$")]
    private static partial Regex LatinOnlyRegex();

    public static IRuleBuilderOptions<T, string> ValidTitle<T>(
        this IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200).WithMessage("Title must be 200 characters or fewer")
            .Matches(LatinOnlyRegex()).WithMessage("Title must contain only Latin characters");
    }

    public static IRuleBuilderOptions<T, string?> ValidDescription<T>(
        this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(1000).WithMessage("Description must be 1000 characters or fewer")
            .Matches(LatinOnlyRegex()).WithMessage("Description must contain only Latin characters");
    }
}
```

**Что здесь важно:**

- **`this IRuleBuilder<T, string>`** — ключевое слово `this` делает метод extension method. Вызывается как `RuleFor(x => x.Title).ValidTitle()`.
- **Дженерик `<T>`** — правило работает с любой командой, не привязано к конкретному типу.
- **`[GeneratedRegex]` + `partial`** — regex компилируется в IL при сборке (source generator), а не парсится каждый раз в рантайме. Обычный `new Regex(...)` создаёт конечный автомат при каждом вызове.
- **`IRuleBuilderOptions` vs `IRuleBuilder`** — `Options` позволяет чейнить `.WithMessage()` после последнего правила.

### 2. Validators

Каждый validator — отдельный класс, наследник `AbstractValidator<T>`:

```csharp
public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
    }
}
```

```csharp
public sealed class UpdateTodoCommandValidator : AbstractValidator<UpdateTodoCommand>
{
    public UpdateTodoCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required");
        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
    }
}
```

**Разница:** Update проверяет ещё `Id` (Guid не должен быть `default`/пустым).

**Регистрация** — одной строкой в `Program.cs`:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateTodoCommandValidator>();
```

DI-контейнер сканирует сборку и находит все `AbstractValidator<T>`. Вручную регистрировать каждый не надо.

### 3. ValidationBehavior (MediatR Pipeline)

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next(ct);

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(ct);
    }
}
```

**Как это работает:**

1. MediatR вызывает `ValidationBehavior.Handle()` ПЕРЕД handler'ом — это `IPipelineBehavior` (аналог middleware, но для MediatR pipeline).
2. DI инжектит `IEnumerable<IValidator<TRequest>>` — все зарегистрированные validators для данного типа запроса.
3. Если validators нет (например, для `GetTodosQuery`) — сразу `next()`, без проверок.
4. Если есть — запускает `ValidateAsync` для каждого параллельно (`Task.WhenAll`).
5. Собирает все ошибки (`failures`). Если хоть одна есть — `throw ValidationException`.
6. Если ошибок нет — `next()` передаёт запрос handler'у.

**Регистрация** в `Program.cs`:

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateTodoCommand>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

`AddOpenBehavior` — регистрирует open generic. MediatR сам подставляет конкретные типы: `ValidationBehavior<CreateTodoCommand, Guid>`, `ValidationBehavior<UpdateTodoCommand, Unit>` и т.д.

### 4. Exception Handler (middleware)

```csharp
app.UseExceptionHandler(err => err.Run(async context =>
{
    var exception = context.Features
        .Get<IExceptionHandlerFeature>()?.Error;

    if (exception is ValidationException validationException)
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";

        var errors = validationException.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        await context.Response.WriteAsJsonAsync(new { errors });
    }
}));
```

**Что происходит:**

1. `ValidationBehavior` бросил `ValidationException` с коллекцией `ValidationFailure`.
2. ASP.NET перехватывает exception и передаёт в `UseExceptionHandler`.
3. Middleware проверяет тип: только `ValidationException` обрабатывается (остальные → стандартный 500).
4. Группирует ошибки по `PropertyName` (Title, Description, Id).
5. Формирует JSON:

```json
{
  "errors": {
    "Title": [
      "Title is required",
      "Title must contain only Latin characters"
    ],
    "Description": [
      "Description must be 1000 characters or fewer"
    ]
  }
}
```

### 5. UI — отображение ошибок

UI не валидирует. Он только отправляет и показывает ответ:

```javascript
const res = await fetch(API, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, description })
});

if (!res.ok) {
    const data = await res.json();
    showErrors('addErrors', data.errors);
    return false;
}
```

Функция `showErrors`:

```javascript
function showErrors(elementId, errors) {
    const el = document.getElementById(elementId);
    if (!errors || Object.keys(errors).length === 0) {
        el.style.display = 'none';
        return;
    }
    el.innerHTML = Object.values(errors)
        .flat()
        .map(m => `<li>${esc(m)}</li>`)
        .join('');
    el.style.display = 'block';
}
```

Берёт объект `errors`, извлекает все массивы сообщений, разворачивает `.flat()`, рендерит `<li>` в красном блоке.

---

## Правила валидации

| Поле | Правило | Сообщение |
|---|---|---|
| Title | Обязательное | Title is required |
| Title | ≤ 200 символов | Title must be 200 characters or fewer |
| Title | Только латиница + цифры + пунктуация | Title must contain only Latin characters |
| Description | ≤ 1000 символов | Description must be 1000 characters or fewer |
| Description | Только латиница + цифры + пунктуация | Description must contain only Latin characters |
| Id (update) | Не пустой Guid | Id is required |

**Latin-only regex:** `^[a-zA-Z0-9\s\p{P}\p{S}]*$`

- `a-zA-Z` — латинские буквы
- `0-9` — цифры
- `\s` — пробелы, табы, переносы
- `\p{P}` — Unicode punctuation (`.`, `,`, `!`, `?`, `-`, `(`, `)` и т.д.)
- `\p{S}` — Unicode symbols (`$`, `+`, `=`, `<`, `>` и т.д.)
- `*` — ноль или более (пустая строка проходит regex, но `NotEmpty` ловит её раньше)

Кириллица, иероглифы, арабский — НЕ проходят.

---

## Почему так устроено

**Почему вся валидация серверная:**
- Клиентская валидация — дублирование. Если правило поменяется, нужно менять в двух местах.
- Клиентскую валидацию можно обойти (curl, Postman, другой клиент). Сервер должен защищать себя сам.
- В Skynet production вся валидация — FluentValidation на сервере. UI показывает ответы.

**Почему shared rules через extension methods:**
- Одно правило, одно место. `ValidTitle()` вызывается из двух validators.
- Если поменяется лимит (200 → 250) — меняется в одном месте.
- Production делает так же: общие правила для Create и Update.

**Почему `[GeneratedRegex]` а не `new Regex()`:**
- Source-generated regex компилируется в IL при сборке.
- `new Regex(pattern)` парсит строку в конечный автомат при каждом создании объекта.
- На hot path (каждый запрос) разница заметна.

**Почему `ValidationBehavior` а не проверка в handler'е:**
- Cross-cutting concern — валидация нужна для всех commands, не только для todo.
- Handler не должен знать про валидацию. Он получает уже проверенные данные.
- Добавить валидацию для новой команды = написать `AbstractValidator<T>`. Pipeline подхватит автоматически.

**Почему exception а не Result pattern:**
- Проще для учебного проекта. В production Skynet используется тот же подход — `throw ValidationException`.
- Exception handler ловит в одном месте, формирует единый формат ответа.
- Result pattern (возврат `Result<T>` вместо exception) — валидная альтернатива, но усложняет каждый handler.

---

## Как добавить новое правило

1. Написать extension method в `TodoValidationRules.cs`:

```csharp
public static IRuleBuilderOptions<T, string> ValidCategory<T>(
    this IRuleBuilder<T, string> rule)
{
    return rule
        .NotEmpty().WithMessage("Category is required")
        .MaximumLength(50).WithMessage("Category must be 50 characters or fewer");
}
```

2. Вызвать в нужных validators:

```csharp
RuleFor(x => x.Category).ValidCategory();
```

3. Всё. `ValidationBehavior` подхватит автоматически, exception handler отформатирует ошибку, UI покажет.

---

## Файлы

| Файл | Что делает |
|---|---|
| `Application/Todos/TodoValidationRules.cs` | Shared правила (extension methods + regex) |
| `Application/Todos/CreateTodoCommandValidator.cs` | Validator для создания |
| `Application/Todos/UpdateTodoCommandValidator.cs` | Validator для обновления |
| `Application/Todos/ValidationBehavior.cs` | MediatR pipeline — запуск валидации |
| `API/Program.cs` | Регистрация (AddValidators, AddOpenBehavior, UseExceptionHandler) |
| `Web/index.html` | Показ ошибок (`showErrors`) |
