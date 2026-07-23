using TodoApi.Domain;

namespace TodoApi.Application;

public interface ITodoRepository
{
    Task<List<TodoItem>> GetAllAsync(CancellationToken ct);
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct);
    Task UpdateAsync(TodoItem item, CancellationToken ct);
    Task DeleteAsync(TodoItem item, CancellationToken ct);
}
