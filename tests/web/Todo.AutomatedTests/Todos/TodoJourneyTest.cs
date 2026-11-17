using Todo.AutomatedTests.Infrastructure;
using Todo.Contracts.Todos;

namespace Todo.AutomatedTests.Todos;

// xUnit runs this class's scenarios sequentially; each gets an empty table and browser context.
public sealed class TodoJourneyTest(TodoAppFixture app) : IClassFixture<TodoAppFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => app.ResetDatabase();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public Task Create_a_task_and_keep_it_after_reload() => app.RunScenario(nameof(Create_a_task_and_keep_it_after_reload), async page =>
    {
        await Expect(page.GetByText("A little space for a fresh start", new() { Exact = true })).ToBeVisibleAsync();
        await Add(page, "  Write the walkthrough  ");
        await Expect(Row(page, "Write the walkthrough")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("New task", new() { Exact = true })).ToHaveValueAsync("");
        await page.ReloadAsync();
        await Expect(Row(page, "Write the walkthrough")).ToBeVisibleAsync();
        await Expect(page.GetByText("1 left to do", new() { Exact = true })).ToBeVisibleAsync();
    });

    [Fact]
    public Task Edit_a_task_and_keep_it_after_reload() => app.RunScenario(nameof(Edit_a_task_and_keep_it_after_reload), async page =>
    {
        await Add(page, "First draft");
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit First draft", Exact = true }).ClickAsync();
        await page.GetByLabel("Edit task title", new() { Exact = true }).FillAsync("Publish the article");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Row(page, "Publish the article")).ToBeVisibleAsync();
        await Expect(Row(page, "First draft")).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await Expect(Row(page, "Publish the article")).ToBeVisibleAsync();
    });

    [Fact]
    public Task Complete_and_reopen_a_task() => app.RunScenario(nameof(Complete_and_reopen_a_task), async page =>
    {
        await Add(page, "Run the browser tests");
        var checkbox = page.GetByRole(AriaRole.Checkbox, new() { Name = "Complete Run the browser tests", Exact = true });
        await checkbox.CheckAsync();
        await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveAttributeAsync("aria-valuenow", "100");
        await page.ReloadAsync();
        await Expect(checkbox).ToBeCheckedAsync();
        await checkbox.UncheckAsync();
        await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveAttributeAsync("aria-valuenow", "0");
        await page.ReloadAsync();
        await Expect(checkbox).Not.ToBeCheckedAsync();
    });

    [Fact]
    public Task Filters_and_counts_follow_completion() => app.RunScenario(nameof(Filters_and_counts_follow_completion), async page =>
    {
        await Add(page, "Build the demo");
        await Add(page, "Capture screenshots");
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Complete Build the demo", Exact = true }).CheckAsync();
        await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveAttributeAsync("aria-valuenow", "50");
        await Filter(page, "Active").ClickAsync();
        await Expect(page.GetByTestId("todo-row")).ToHaveCountAsync(1);
        await Expect(Row(page, "Capture screenshots")).ToBeVisibleAsync();
        await Filter(page, "Completed").ClickAsync();
        await Expect(page.GetByTestId("todo-row")).ToHaveCountAsync(1);
        await Expect(Row(page, "Build the demo")).ToBeVisibleAsync();
        await Filter(page, "All tasks").ClickAsync();
        await Expect(page.GetByTestId("todo-row")).ToHaveCountAsync(2);
        await Expect(Filter(page, "All tasks")).ToContainTextAsync("2");
        await Expect(Filter(page, "Active")).ToContainTextAsync("1");
        await Expect(Filter(page, "Completed")).ToContainTextAsync("1");
    });

    [Fact]
    public Task Delete_a_task_permanently() => app.RunScenario(nameof(Delete_a_task_permanently), async page =>
    {
        await Add(page, "Remove this task");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete Remove this task", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("todo-row")).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await Expect(page.GetByText("A little space for a fresh start", new() { Exact = true })).ToBeVisibleAsync();
    });

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("overlong")]
    public Task Invalid_titles_do_not_create_tasks(string value) => app.RunScenario(nameof(Invalid_titles_do_not_create_tasks), async page =>
    {
        await page.GetByLabel("New task", new() { Exact = true }).FillAsync(value == "overlong" ? new string('x', 201) : value);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add task", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(TodoRules.TitleError);
        await Expect(page.GetByTestId("todo-row")).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await Expect(page.GetByText("A little space for a fresh start", new() { Exact = true })).ToBeVisibleAsync();
    });

    [Fact]
    public Task Invalid_edit_can_be_corrected_or_cancelled() => app.RunScenario(nameof(Invalid_edit_can_be_corrected_or_cancelled), async page =>
    {
        await Add(page, "Keep this title");
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit Keep this title", Exact = true }).ClickAsync();
        await page.GetByLabel("Edit task title", new() { Exact = true }).FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync(TodoRules.TitleError);
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await page.ReloadAsync();
        await Expect(Row(page, "Keep this title")).ToBeVisibleAsync();
    });

    [Fact]
    public Task Create_and_complete_a_task_on_mobile() => app.RunScenario(nameof(Create_and_complete_a_task_on_mobile), async page =>
    {
        await Add(page, "A small screen, a real task");
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Complete A small screen, a real task", Exact = true }).CheckAsync();
        await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveAttributeAsync("aria-valuenow", "100");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    }, mobile: true);

    private static ILocator Row(IPage page, string title) => page.GetByTestId("todo-row").Filter(new() { HasText = title });
    private static ILocator Filter(IPage page, string name) => page.GetByRole(AriaRole.Navigation, new() { Name = "Task filters" })
        .GetByRole(AriaRole.Button).Filter(new() { HasText = name });

    private static async Task Add(IPage page, string title)
    {
        await page.GetByLabel("New task", new() { Exact = true }).FillAsync(title);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add task", Exact = true }).ClickAsync();
        await Expect(Row(page, title.Trim())).ToBeVisibleAsync();
    }
}
