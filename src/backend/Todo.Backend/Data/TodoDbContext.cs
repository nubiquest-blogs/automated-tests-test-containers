using Microsoft.EntityFrameworkCore;
using Todo.Contracts.Todos;

namespace Todo.Backend.Data;

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoEntity> Todos => Set<TodoEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var todo = modelBuilder.Entity<TodoEntity>();
        todo.ToTable("Todos");
        todo.HasKey(x => x.Id);
        todo.Property(x => x.Title).HasMaxLength(TodoRules.MaxTitleLength).IsRequired();
    }
}
