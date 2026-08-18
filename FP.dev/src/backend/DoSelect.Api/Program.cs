using DoSelect.Api.Common;
using DoSelect.Api.Observability;
using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Email;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddApiFoundation();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IEmailSender>(services =>
{
    var emailEnabled = builder.Configuration.GetValue<bool>("Features:EmailEnabled");
    return emailEnabled
        ? new SmtpEmailSender(services.GetRequiredService<IOptions<SmtpEmailOptions>>().Value)
        : new LocalEmailSender();
});

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
