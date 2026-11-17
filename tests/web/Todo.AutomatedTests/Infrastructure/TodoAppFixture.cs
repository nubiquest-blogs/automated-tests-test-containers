using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todo.Contracts.Todos;

namespace Todo.AutomatedTests.Infrastructure;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class TodoAppFixture : IAsyncLifetime
{
    private DistributedApplication? _application;
    private IDistributedApplicationTestingBuilder? _builder;
    private HttpClient? _backend;
    private IBrowser? _browser;
    private IPlaywright? _playwright;

    private Uri BaseAddress { get; set; } = null!;

    public async Task InitializeAsync()
    {
        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var builder = _builder = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.Todo_Host>(cancellationToken: startup.Token);
            builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

            // Challenge: the real AppHost configures PostgreSQL with a persistent lifetime
            // and a data volume. Tests reuse that topology, but must isolate the database:
            // ResetDatabase deletes every task before each scenario, so sharing the
            // development database would erase the developer's saved tasks.

            // Get the resource already declared by the AppHost; this does not add a second one.
            var postgres = builder.CreateResourceBuilder<PostgresServerResource>("postgres");

            // Override the persistent lifetime so Aspire removes this container at teardown.
            // A unique name keeps it separate from the development container and other test runs.
            postgres.WithLifetime(ContainerLifetime.Session)
                .WithContainerName($"todo-tests-{Guid.NewGuid():N}");

            // A new container alone is not enough: mounting the development volume would
            // still expose the same saved data. Remove mounts so this run starts with an
            // empty database, initialized by the backend's normal EF migrations.
            // Materialize a copy with ToArray because we remove items from the original collection.
            foreach (var mount in postgres.Resource.Annotations.OfType<ContainerMountAnnotation>().ToArray())
                postgres.Resource.Annotations.Remove(mount);

            // Leave fixed development ports available, including when the app is already running.
            foreach (var resource in builder.Resources)
            foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
                endpoint.Port = null;

            _application = await builder.BuildAsync(startup.Token);
            await _application.StartAsync(startup.Token);
            await _application.ResourceNotifications.WaitForResourceHealthyAsync("web", startup.Token);
            BaseAddress = _application.GetEndpoint("web", "http");
            _backend = _application.CreateHttpClient("backend", "http");

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

    // Reset through the real API; the fixture doesn't depend on backend internals or EF.
    public async Task ResetDatabase()
    {
        var backend = _backend ?? throw new InvalidOperationException("The test application has not started.");
        var todos = await backend.GetFromJsonAsync<List<TodoResponse>>("/api/todos") ?? [];
        foreach (var todo in todos)
        {
            using var response = await backend.DeleteAsync($"/api/todos/{todo.Id}");
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task RunScenario(string name, Func<IPage, Task> scenario)
    {
        await using var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = BaseAddress.ToString(),
            ViewportSize = new() { Width = 1440, Height = 1000 }
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
            _backend?.Dispose();
            _backend = null;
            try
            {
                if (_application is not null)
                {
                    await _application.DisposeAsync();
                    _application = null;
                }
            }
            finally
            {
                if (_builder is not null)
                {
                    await _builder.DisposeAsync();
                    _builder = null;
                }
            }
        }
    }
}
