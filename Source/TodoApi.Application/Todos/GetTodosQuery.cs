using MediatR;
using TodoApi.Domain;

namespace TodoApi.Application.Todos;

public sealed record GetTodosQuery : IRequest<List<TodoItem>>;

public sealed class GetTodosHandler(ITodoRepository repository) : IRequestHandler<GetTodosQuery, List<TodoItem>>
{
    public Task<List<TodoItem>> Handle(GetTodosQuery request, CancellationToken ct)
        => repository.GetAllAsync(ct);
}
