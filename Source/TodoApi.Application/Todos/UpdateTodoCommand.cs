using MediatR;
using TodoApi.Domain;

namespace TodoApi.Application.Todos;

public sealed record UpdateTodoCommand(Guid Id, string Title, string? Description, bool IsCompleted) : IRequest<TodoItem?>;

public sealed class UpdateTodoHandler(ITodoRepository repository) : IRequestHandler<UpdateTodoCommand, TodoItem?>
{
    public async Task<TodoItem?> Handle(UpdateTodoCommand request, CancellationToken ct)
    {
        var item = await repository.GetByIdAsync(request.Id, ct);
        if (item is null)
            return null;

        item.Title = request.Title;
        item.Description = request.Description;

        if (request.IsCompleted && !item.IsCompleted)
            item.CompletedAt = DateTime.UtcNow;

        if (!request.IsCompleted)
            item.CompletedAt = null;

        item.IsCompleted = request.IsCompleted;

        await repository.UpdateAsync(item, ct);
        return item;
    }
}
