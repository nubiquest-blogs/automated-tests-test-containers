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
