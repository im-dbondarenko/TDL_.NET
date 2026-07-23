using Microsoft.EntityFrameworkCore;
using TodoApi.Application;
using TodoApi.Domain;

namespace TodoApi.Infrastructure;

public sealed class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public Task<List<TodoItem>> GetAllAsync(CancellationToken ct)
        => db.Todos.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Todos.FindAsync([id], ct).AsTask();

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public Task UpdateAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Update(item);
        return db.SaveChangesAsync(ct);
    }

    public Task DeleteAsync(TodoItem item, CancellationToken ct)
    {
        db.Todos.Remove(item);
        return db.SaveChangesAsync(ct);
    }
}
