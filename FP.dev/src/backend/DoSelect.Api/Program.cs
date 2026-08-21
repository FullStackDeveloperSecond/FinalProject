using DoSelect.Api.Common;
using DoSelect.Api.Observability;
using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Email;
using DoSelect.Infrastructure.Files;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Seeding;
using DoSelect.Api.Security;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddApiFoundation();
builder.Services.AddOpenApi();
builder.Services.AddDoSelectPersistence(builder.Configuration);
builder.Services.AddDoSelectFileStorage(builder.Configuration);
builder.Services.AddDoSelectSecurity(builder.Environment, builder.Configuration);
builder.Services.AddSingleton<IEmailSender>(services =>
{
    var emailEnabled = builder.Configuration.GetValue<bool>("Features:EmailEnabled");
    return emailEnabled
        ? new SmtpEmailSender(services.GetRequiredService<IOptions<SmtpEmailOptions>>().Value)
        : new LocalEmailSender();
});

var app = builder.Build();

if (args.Contains("--seed-minimal", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<MinimalDevelopmentDataSeeder>();
    var result = await seeder.SeedAsync();
    app.Logger.LogInformation(
        "Minimal development seed completed. RolesCreated={RolesCreated}, UsersCreated={UsersCreated}, ProfilesCreated={ProfilesCreated}, CatalogRecordsCreated={CatalogRecordsCreated}",
        result.RolesCreated,
        result.UsersCreated,
        result.ProfilesCreated,
        result.CatalogRecordsCreated);
    return;
}

app.UseRequestObservability();
app.UseApiFoundation();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors(SecurityServiceCollectionExtensions.FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapObservabilityHealthChecks();

app.Run();

public partial class Program;
