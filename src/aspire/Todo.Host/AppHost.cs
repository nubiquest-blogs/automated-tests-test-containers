var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", "Password123", secret: true);
var database = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17-alpine")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume().AddDatabase("todos");
var backend = builder.AddProject<Projects.Todo_Backend>("backend")
    .WithReference(database).WaitFor(database).WithHttpHealthCheck("/health");
builder.AddProject<Projects.Todo_Web>("web")
    .WithReference(backend).WaitFor(backend).WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
