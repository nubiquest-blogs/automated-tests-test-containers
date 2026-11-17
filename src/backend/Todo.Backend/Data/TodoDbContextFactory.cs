using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Todo.Backend.Data;

// Migration generation needs the model, not a running Aspire host or database.
public sealed class TodoDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__todos")
            ?? "Host=localhost;Database=todos;Username=postgres;Password=postgres";
        return new TodoDbContext(new DbContextOptionsBuilder<TodoDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
