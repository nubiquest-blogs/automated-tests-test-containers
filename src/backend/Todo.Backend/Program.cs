using Todo.Backend.Bootstrappers;
using Todo.Backend.Todos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddTodoDatabase();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapTodoEndpoints();
app.Run();

// Distinct entry points let the test project host both applications.
public partial class BackendProgram;
