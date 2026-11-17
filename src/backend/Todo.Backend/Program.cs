using Microsoft.EntityFrameworkCore;
using Todo.Backend.Data;
using Todo.Backend.Todos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("todos")
        ?? throw new InvalidOperationException("ConnectionStrings:todos is required.")));
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");

var app = builder.Build();
app.UseExceptionHandler();
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.MigrateAsync();
}
app.MapDefaultEndpoints();
app.MapTodoEndpoints();
app.Run();

// Distinct entry points let the test project host both applications.
public partial class BackendProgram;
