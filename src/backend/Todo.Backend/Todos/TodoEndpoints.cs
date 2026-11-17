using Microsoft.EntityFrameworkCore;
using Todo.Backend.Data;
using Todo.Contracts.Todos;

namespace Todo.Backend.Todos;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/todos");

        group.MapGet("/", async (TodoDbContext db, CancellationToken cancellationToken) =>
            await db.Todos.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Select(x => new TodoResponse(x.Id, x.Title, x.IsCompleted, x.CreatedAt))
                .ToListAsync(cancellationToken));

        group.MapPost("/", async (CreateTodoRequest request, TodoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!TodoRules.IsValidTitle(request.Title)) return InvalidTitle();
            var todo = new TodoEntity { Title = request.Title!.Trim() };
            db.Todos.Add(todo);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/todos/{todo.Id}", Response(todo));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateTodoRequest request, TodoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!TodoRules.IsValidTitle(request.Title)) return InvalidTitle();
            var todo = await db.Todos.FindAsync([id], cancellationToken);
            if (todo is null) return Results.NotFound();
            todo.Title = request.Title!.Trim();
            todo.IsCompleted = request.IsCompleted;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(Response(todo));
        });

        group.MapDelete("/{id:guid}", async (Guid id, TodoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var deleted = await db.Todos.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static IResult InvalidTitle() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["title"] = [TodoRules.TitleError] });

    private static TodoResponse Response(TodoEntity todo) =>
        new(todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt);
}
