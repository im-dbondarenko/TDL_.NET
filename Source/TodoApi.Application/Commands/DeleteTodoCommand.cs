using MediatR;

namespace TodoApi.Application.Commands;

public sealed record DeleteTodoCommand(int Id) : IRequest<bool>;

public sealed class DeleteTodoHandler(ITodoRepository repository) : IRequestHandler<DeleteTodoCommand, bool>
{
    public async Task<bool> Handle(DeleteTodoCommand request, CancellationToken ct)
    {
        var item = await repository.GetByIdAsync(request.Id, ct);
        if (item is null)
            return false;

        await repository.DeleteAsync(item, ct);
        return true;
    }
}
