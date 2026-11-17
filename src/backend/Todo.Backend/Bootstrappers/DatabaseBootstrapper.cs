using Todo.Backend.Data;
using Todo.Backend.HostedServices;

namespace Todo.Backend.Bootstrappers;

public static class DatabaseBootstrapper
{
    public static IHostApplicationBuilder AddTodoDatabase(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<TodoDbContext>("todos");
        builder.Services.AddHostedService<MigrationHostedService>();
        return builder;
    }
}
