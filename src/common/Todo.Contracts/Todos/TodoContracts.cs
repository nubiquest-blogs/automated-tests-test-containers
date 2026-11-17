namespace Todo.Contracts.Todos;

public sealed record TodoResponse(Guid Id, string Title, bool IsCompleted, DateTimeOffset CreatedAt);
public sealed record CreateTodoRequest(string? Title);
public sealed record UpdateTodoRequest(string? Title, bool IsCompleted);

public static class TodoRules
{
    public const int MaxTitleLength = 200;
    public const string TitleError = "Enter a task between 1 and 200 characters.";

    public static bool IsValidTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= MaxTitleLength;
}
