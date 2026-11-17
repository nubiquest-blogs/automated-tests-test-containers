using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Todo.Backend.Data;

namespace Todo.AutomatedTests.Infrastructure;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class TodoAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("todo_tests")
        .WithUsername("todo")
        .WithPassword("todo-test-password")
        .Build();
    private WebApplicationFactory<BackendProgram>? _backend;
    private IBrowser? _browser;
    private IPlaywright? _playwright;
    private WebApplicationFactory<WebProgram>? _web;
    
    
    private Uri BaseAddress { get; set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            _backend = new BackendFactory(_postgres.GetConnectionString());
            var backendAddress = StartOnKestrel(_backend);
            _web = new WebFactory(backendAddress);
            BaseAddress = StartOnKestrel(_web);
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = Environment.GetEnvironmentVariable("HEADED") != "1"
            });
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    // Called before each scenario, never against the Aspire development database.
    public async Task ResetDatabase()
    {
        await using var scope = _backend!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Todos.ExecuteDeleteAsync();
    }

    public async Task RunScenario(string name, Func<IPage, Task> scenario, bool mobile = false)
    {
        await using var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = BaseAddress.ToString(),
            ViewportSize = new() { Width = mobile ? 390 : 1440, Height = mobile ? 844 : 1000 }
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var failed = false;
        try
        {
            await page.GotoAsync("/");
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add task", Exact = true })).ToBeEnabledAsync();
            await scenario(page);
            Assert.Empty(errors);
        }
        catch
        {
            failed = true;
            var directory = Path.Combine("TestResults", name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            // Diagnostics must not replace the original assertion failure.
            try { await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "failure.png"), FullPage = true }); }
            catch (PlaywrightException) { }
            try { await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") }); }
            catch (PlaywrightException) { }
            throw;
        }
        finally
        {
            if (!failed) await context.Tracing.StopAsync();
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_browser is not null) { await _browser.DisposeAsync(); _browser = null; }
        }
        finally
        {
            _playwright?.Dispose();
            _playwright = null;
            try
            {
                if (_web is not null) { await _web.DisposeAsync(); _web = null; }
            }
            finally
            {
                try
                {
                    if (_backend is not null) { await _backend.DisposeAsync(); _backend = null; }
                }
                finally { await _postgres.DisposeAsync(); }
            }
        }
    }

    private static Uri StartOnKestrel<T>(WebApplicationFactory<T> factory) where T : class
    {
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        return client.BaseAddress!;
    }

    private sealed class BackendFactory(string connectionString) : WebApplicationFactory<BackendProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:todos", connectionString);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        }
    }

    private sealed class WebFactory(Uri backendAddress) : WebApplicationFactory<WebProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("services:backend:http:0", backendAddress.ToString());
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.UseStaticWebAssets();
        }
    }
}
