using MediatR;
using TodoApi.Domain;

namespace TodoApi.Application.Queries;

public sealed record GetTodoByIdQuery(int Id) : IRequest<TodoItem?>;

public sealed class GetTodoByIdHandler(ITodoRepository repository) : IRequestHandler<GetTodoByIdQuery, TodoItem?>
{
    public Task<TodoItem?> Handle(GetTodoByIdQuery request, CancellationToken ct)
        => repository.GetByIdAsync(request.Id, ct);
}
