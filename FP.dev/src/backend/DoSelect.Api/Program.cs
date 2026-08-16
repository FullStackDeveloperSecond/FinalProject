using DoSelect.Api.Common;
using DoSelect.Api.Observability;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddApiFoundation();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseRequestObservability();
app.UseApiFoundation();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapObservabilityHealthChecks();

app.Run();

public partial class Program;
