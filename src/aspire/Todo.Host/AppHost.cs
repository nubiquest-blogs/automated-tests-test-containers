var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("postgres").WithImageTag("17-alpine")
    .WithDataVolume().AddDatabase("todos");
var backend = builder.AddProject<Projects.Todo_Backend>("backend")
    .WithReference(database).WaitFor(database).WithHttpHealthCheck("/health");
builder.AddProject<Projects.Todo_Web>("web")
    .WithReference(backend).WaitFor(backend).WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();
