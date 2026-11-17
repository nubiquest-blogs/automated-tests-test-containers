using Todo.Web.Components;
using Todo.Web.Todos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient<TodoApiClient>(client => client.BaseAddress = new Uri("http://backend"));

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error");
app.UseAntiforgery();
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class WebProgram;
