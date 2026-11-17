# Automated Testing Using Aspire and Playwright in .NET 10

Manual testing is exhausting and very repetitive. After N attempts, tester can lose focus and make mistakes. On the other hand, manual tests and integration tests are extremely useful to validate the code. However, they do not provide the full confidence over the expectations. That is why, end-to-end automated testing can be extremely useful to assess the quality and to guarantee no regressions are happening after pushing new changes.

In this walkthrough, we'll build automated tests using [Aspire's testing support](https://aspire.dev/testing/manage-app-host/) and [Playwright for .NET](https://playwright.dev/dotnet/). The browser will use a real Blazor Server application, which calls a minimal API, which writes to PostgreSQL. The test run starts everything it needs and cleans up afterwards.

The demo is **Daymark**, a small TODO app built with .NET 10, Entity Framework Core, PostgreSQL and Tailwind CSS. Aspire starts the application for local development and automated testing. Each test run gets its own isolated environment.

![Daymark TODO application with active and completed tasks](images/daymark.jpg)

This tutorial covers:

- Using **`Aspire.Hosting.Testing`**, to start and control the complete application from our tests.
- Letting Aspire manage PostgreSQL, application startup and endpoint discovery.
- Writing Playwright tests around user actions and persisted results.
- Keeping scenarios independent and saving useful diagnostics when they fail.

[`Aspire.Hosting.Testing`](https://aspire.dev/testing/manage-app-host/) connects our test suite to the real AppHost. Its `DistributedApplicationTestingBuilder` lets us load the application definition, adjust resources for testing, and start the backend, frontend and PostgreSQL together. The package also provides helpers to wait for healthy resources, discover their URLs and create HTTP clients. Disposing the test application shuts down its session-scoped resources. This gives Playwright a running application to exercise, with xUnit managing the test lifecycle.

## Running the TODO app

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [Node.js](https://nodejs.org/) 20 or later, and a running Docker-compatible container runtime. Docker Desktop works on macOS and Windows; Linux containers must be available. The repository pins a .NET 10 SDK baseline and allows newer .NET 10 feature bands.

Run the following commands from the repository root:

```bash
npm ci --prefix src/web/Todo.Web
npm run build --prefix src/web/Todo.Web
dotnet tool restore
dotnet build Todo.slnx
```

Install the Chromium build required by our Playwright package. On macOS or Linux:

```bash
./scripts/playwright.sh install chromium
```

On Linux, install its operating system dependencies as well if needed:

```bash
./scripts/playwright.sh install --with-deps chromium
```

On Windows, use PowerShell:

```powershell
pwsh tests/web/Todo.AutomatedTests/bin/Debug/net10.0/playwright.ps1 install chromium
```

The shell helper uses the Playwright CLI shipped with the .NET package. This matters: a separately installed Playwright CLI might download a different browser version.

Launch the app:

```bash
dotnet run --project src/aspire/Todo.Host
```

Open **http://localhost:5200** for the frontend. Aspire prints the dashboard login link in the terminal. The backend is available at **http://localhost:5201**. This local demo uses HTTP and enables Aspire's unsecured transport setting in its development launch profile.

The development database uses a Docker volume, so tasks survive application restarts. The database starts empty on the first run; the screenshots contain tasks added through the browser.

## The TODO app solution

The solution has six projects:

```text
src/
  aspire/
    Todo.Host                 Development and test orchestration
    Todo.ServiceDefaults      Discovery, health, resilience and telemetry
  backend/
    Todo.Backend              Minimal APIs, EF context and migrations
  common/
    Todo.Contracts            Shared API records and title rules
  web/
    Todo.Web                  Blazor Server and Tailwind
tests/
  web/
    Todo.AutomatedTests       Playwright scenarios and Aspire test fixture
```

The frontend is an HTTP client of the backend. Only the backend application opens database connections.

Our only application table is `Todos`, with `Id`, `Title`, `IsCompleted` and `CreatedAt`. EF also creates its migration history table. The API has four operations:

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/todos` | List tasks, newest first |
| POST | `/api/todos` | Create a task and return it with status 201 |
| PUT | `/api/todos/{id}` | Update title and completion status |
| DELETE | `/api/todos/{id}` | Delete a task and return status 204 |


## The role of Aspire

The AppHost starts PostgreSQL and connects the applications:

```csharp
var password = builder.AddParameter("postgres-password", "Password123", secret: true);
var postgres = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17-alpine");

if (builder.Configuration.GetValue("UsePersistence", true))
{
    postgres.WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}

var database = postgres.AddDatabase("todos");
var backend = builder.AddProject<Projects.Todo_Backend>("backend")
    .WithReference(database).WaitFor(database).WithHttpHealthCheck("/health");
builder.AddProject<Projects.Todo_Web>("web")
    .WithReference(backend).WaitFor(backend).WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();
```

The local database uses `Password123`. Our `UsePersistence` setting defaults to `true`, enabling a persistent container and data volume so tasks survive development restarts.

Both applications reference `Todo.ServiceDefaults`. It adds service discovery, HTTP resilience, health checks and OpenTelemetry. When Aspire supplies an OTLP endpoint, telemetry is exported to the dashboard. The backend readiness check also checks database connectivity.

The web client uses `http://backend` as its base address. Service discovery resolves that name to the endpoint supplied by Aspire. HTTP retries are disabled for unsafe methods so that a failed POST isn't automatically sent again and potentially creates a duplicate task.

For tests, we reuse this same AppHost. Aspire still supplies the database connection and resolves the backend service address, so we exercise the application wiring as well as the user interface. The test fixture passes `UsePersistence=false` to keep its database isolated from development. Aspire testing handles port allocation. We'll look at this configuration in the Challenges section.

## Validating the application with Aspire testing

The test project uses xUnit to discover and run the scenarios, `Aspire.Hosting.Testing` to manage the application, and `Microsoft.Playwright` to drive Chromium. It references `Todo.Host` and `Todo.Contracts`.

In `TodoAppFixture.InitializeAsync`, we create a testing builder for the actual AppHost:

```csharp
using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var builder = _builder = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Todo_Host>(["UsePersistence=false"], cancellationToken: startup.Token);
```

The generated `Projects.Todo_Host` type identifies the AppHost project. The fixture keeps the builder in a field because it needs to stay alive for the entire suite.

With development persistence disabled through configuration, we start the application and wait for the frontend to become healthy:

```csharp
_application = await builder.BuildAsync(startup.Token);
await _application.StartAsync(startup.Token);
await _application.ResourceNotifications.WaitForResourceHealthyAsync("web", startup.Token);
BaseAddress = _application.GetEndpoint("web", "http");
_backend = _application.CreateHttpClient("backend", "http");
```

The AppHost already defines the dependency order: PostgreSQL, the `todos` database, the backend and then the frontend. The backend applies EF migrations during startup. Waiting for the frontend's healthy state lets us open the browser after its dependencies are ready. The two-minute cancellation token bounds this startup sequence.

`GetEndpoint` supplies the real frontend URL, and `CreateHttpClient` supplies a client for the backend. We don't hardcode the test application's URLs. See [accessing Aspire resources in tests](https://aspire.dev/testing/accessing-resources/) for these helpers.

The browser setup is the usual Playwright code:

```csharp
_playwright = await Playwright.CreateAsync();
_browser = await _playwright.Chromium.LaunchAsync(new()
{
    Headless = Environment.GetEnvironmentVariable("HEADED") != "1"
});
```

The resulting path is the same one a person uses:

```text
Chromium → Blazor Server → HTTP API → EF Core → PostgreSQL
```

Aspire starts the real backend and frontend processes. Blazor's circuit, HTTP serialization, routing and database writes are all involved. Nothing in that path is replaced by a mock.

## Keeping scenarios independent

Starting the application for every assertion would be expensive. We share `TodoAppFixture` across the test class using xUnit's `IClassFixture<TodoAppFixture>`. Before each scenario, the test class calls `ResetDatabase`; each scenario also gets a fresh browser context.

The reset goes through the backend's normal API:

```csharp
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
```

This uses the existing list and delete endpoints, so the fixture doesn't need access to the backend's context or a special reset endpoint. It leaves the migrated schema intact. For this small TODO demo, deleting the few tasks created by the previous scenario is enough.

All scenarios belong to the same xUnit class, which executes them sequentially. Parallel scenarios would need separate databases or another isolation strategy because one scenario's reset must not delete another scenario's data.

A new browser context keeps cookies, storage and pages seperate between scenarios. The shared Chromium process avoids paying browser startup cost for every test.

## Challenges

### Keeping the development database out of the tests

The AppHost configures PostgreSQL with a persistent container and a data volume. This is useful during development because tasks survive restarts. For tests, it creates a problem: our reset deletes every task before each scenario. Reusing that database could erase the developer's saved tasks.

We make persistence an explicit AppHost configuration setting:

```csharp
if (builder.Configuration.GetValue("UsePersistence", true))
{
    postgres.WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}
```

`UsePersistence` is our own setting, enabled by default for development. The test fixture disables it when creating the testing builder:

```csharp
var builder = _builder = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.Todo_Host>(["UsePersistence=false"], cancellationToken: startup.Token);
```

The test AppHost therefore never attaches the saved development volume or enables persistent container lifetime. PostgreSQL uses Aspire's default session lifetime, so each test run gets a fresh container that is removed when the test application is disposed. Aspire handles container naming; the fixture doesn't need to edit resource annotations.

Each run starts with an empty PostgreSQL database. Aspire creates `todos`, and the backend's normal EF migrations create the application table. We reuse the real application definition while keeping the test data disposable. This configuration-based approach also follows [Aspire's guidance for disabling volumes in tests](https://aspire.dev/testing/manage-app-host/).

### Resetting data between scenarios

A fresh database per test run doesn't mean a fresh database per scenario. We share the running application across the class, so tasks created by one scenario would remain for the next one. Creating a new browser context only resets browser state; it doesn't clear PostgreSQL. This could break empty-state assertions or make task counts depend on execution order.

With Aspire, the backend runs in its own process. The AppHost's service provider contains orchestration services, not the backend's `TodoDbContext`. Our previous approach of resolving the context from an in-process application host therefore no longer fits.

We solved this through the existing API. The fixture gets a backend client with `_application.CreateHttpClient("backend", "http")`. The `ResetDatabase` method shown above lists the tasks and deletes each one, checking that every deletion succeeds. No additional reset endpoint or direct database connection is needed.

The test class calls this reset before every scenario through xUnit's asynchronous initialization:

```csharp
public Task InitializeAsync() => app.ResetDatabase();
```

Resetting before the next scenario also handles tasks left behind by a failed test. The database schema and running processes remain available, while each scenario starts with an empty task list. Scenarios in this class run sequentially so their resets cannot interfere with each other.

### Avoiding fixed-port conflicts

Development uses stable frontend and backend ports. The Aspire testing builder randomizes resource ports by default, allowing a test instance to run alongside the development app. We rely on that built-in behavior rather than assigning ports in the fixture. See the [Aspire testing configuration options](https://aspire.dev/testing/overview/#configuration-options).

`GetEndpoint("web", "http")` returns the actual frontend URL. The frontend continues to discover the backend through Aspire's resource references, so the browser tests don't depend on the development ports.

### Keeping the testing builder alive

It is tempting to declare the testing builder with `await using` inside `InitializeAsync`. That method returns before the scenarios execute, so its local using scope would dispose the builder too early. In our first run, this caused an `ObjectDisposedException` from the HTTP client's meter factory.

The fixture keeps both the testing builder and the distributed application in fields. Teardown disposes the browser and HTTP client, then the application, then the builder. The same cleanup runs if initialization fails. This keeps Aspire's shared services available until the scenarios finish.

## Writing the first browser test

Let's check that a task is actually saved. This is the creation scenario from the suite:

```csharp
[Fact]
public Task Create_a_task_and_keep_it_after_reload() =>
    app.RunScenario(nameof(Create_a_task_and_keep_it_after_reload), async page =>
    {
        await Expect(page.GetByText("A little space for a fresh start",
            new() { Exact = true })).ToBeVisibleAsync();
        await Add(page, "  Write the walkthrough  ");
        await Expect(Row(page, "Write the walkthrough")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("New task", new() { Exact = true }))
            .ToHaveValueAsync("");
        await page.ReloadAsync();
        await Expect(Row(page, "Write the walkthrough")).ToBeVisibleAsync();
        await Expect(page.GetByText("1 left to do", new() { Exact = true }))
            .ToBeVisibleAsync();
    });
```

`RunScenario` opens the page, waits for the Add button to become enabled, runs the scenario, and checks for uncaught browser errors. `Add` fills the labeled input, clicks Add task and waits for the corresponding row.

The reload is a useful part of this test. Seeing a new row immediately after clicking Add could only demonstrate an in-memory UI update. Seeing it after a reload checks that the frontend can read it back through the backend and PostgreSQL.

Notice the spaces around the input title: the same scenario also checks trimming.

## Completion, filters and validation

The other scenarios use the same fixture and browser helpers. For example, completing a task is a user interaction followed by an observable result:

```csharp
var checkbox = page.GetByRole(AriaRole.Checkbox, new()
{
    Name = "Complete Run the browser tests",
    Exact = true
});
await checkbox.CheckAsync();
await Expect(page.GetByRole(AriaRole.Progressbar))
    .ToHaveAttributeAsync("aria-valuenow", "100");
await page.ReloadAsync();
await Expect(checkbox).ToBeCheckedAsync();
```

The filter scenario creates two tasks, completes one, and checks the All, Active and Completed views together with their counts.

![Completed filter showing two finished tasks](images/completed.jpg)

We also cover empty, whitespace-only and overlong titles. These browser scenarios check that the user sees a validation message and no task is created. They exercise the frontend validation; they don't claim to test every backend validation response independently. The backend applies the shared rule as well.

The complete suite contains nine scenarios, including the three invalid-title cases:

| Scenario | What we check |
| --- | --- |
| Create | Empty state, trimmed title, cleared input and persistence |
| Edit | Updated title survives reload |
| Complete and reopen | Completion state and progress survive reload |
| Filters | Visible rows and counts follow completion |
| Delete | Removed task stays removed after reload |
| Invalid titles, three cases | Validation message and no persisted task |
| Invalid edit | Cancelling preserves the saved title |

## Waiting for behavior

Browser tests become frustrating when they assume the application always responds within a fixed delay. Playwright's locators and [retrying assertions](https://playwright.dev/dotnet/docs/test-assertions) wait for the expected state, so we can write:

```csharp
await Expect(Row(page, "Publish the article")).ToBeVisibleAsync();
```

There are no `Thread.Sleep` or `Task.Delay` calls in the tests. We wait for a visible row, a checked checkbox or an updated progress value.

Most selectors use labels and roles, such as `GetByLabel("New task")`. This keeps the test close to what the user sees. The task row has one explicit `data-testid` so assertions can scope themselves to a particular task without depending on Tailwind classes.

## Running and investigating the tests

With Docker running and Chromium installed:

```bash
dotnet test Todo.slnx
```

You don't have to run the AppHost manually first. The test fixture starts it through Aspire's testing builder, with its own PostgreSQL container and application processes. The dashboard is disabled by default for these test runs. The isolated container and dynamic ports also let the tests run alongside the development app.

To watch the browser on macOS or Linux:

```bash
HEADED=1 dotnet test Todo.slnx
```

In PowerShell:

```powershell
$env:HEADED = "1"
dotnet test Todo.slnx
Remove-Item Env:HEADED
```

When a scenario fails, `RunScenario` attempts to save `failure.png` and `trace.zip` in a unique directory under `TestResults` relative to the test runner's working directory. These artifacts are ignored by Git. To open a trace:

```bash
./scripts/playwright.sh show-trace /path/to/TestResults/scenario/trace.zip
```

The trace includes browser actions, DOM snapshots and screenshots. It is a lot more useful than only knowing that a button click timed out. The fixture disposes browser contexts after each scenario. At the end of the suite it disposes the browser, HTTP client, distributed application and testing builder. Aspire removes the session-scoped database container; the development volume stays intact.

If Chromium cannot be found, rerun the browser installation command after building. If the container cannot start, check that Docker is running and that the current user can access its socket. The first run may take longer while images and browsers are downloaded.

## Wrap-up

In this article, I have shown how we use Aspire and Playwright for automated testing of a .net app composed of a backend and a frontend. Aspire starts the full application and its database, while Playwright checks the user journeys through a real browser.

These tests bring a lot of confidence. They are most of the time much more complex than the case we are showing in this walkthrough.

At the companies I work with, we use also playwright for nightly runs and smoke tests to check that we did not introduce any issues to deployed apps.

The code of the app can be found [here](https://github.com/nubiquest-blogs/automated-tests-test-containers)

Enjoy!
