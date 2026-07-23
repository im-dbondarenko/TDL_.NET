using MediatR;
using TodoApi.Domain;

namespace TodoApi.Application.Todos;

public sealed record CreateTodoCommand(string Title, string? Description) : IRequest<TodoItem>;

public sealed class CreateTodoHandler(ITodoRepository repository) : IRequestHandler<CreateTodoCommand, TodoItem>
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
