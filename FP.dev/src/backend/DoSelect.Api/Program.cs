using DoSelect.Api.Common;
using DoSelect.Api.Ai;
using DoSelect.Api.Observability;
using DoSelect.Api.Security;
using DoSelect.Application;
using DoSelect.Application.Notifications;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Admin;
using DoSelect.Infrastructure.Catalog;
using DoSelect.Infrastructure.Email;
using DoSelect.Infrastructure.Files;
using DoSelect.Infrastructure.Idempotency;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Persistence.Seeding;
using DoSelect.Infrastructure.Refunds;
using DoSelect.Infrastructure.Shopping;
using Microsoft.AspNetCore.Authentication;
using DoSelect.Infrastructure.Persistence.Support;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddApiFoundation();
builder.Services.AddAiSupport();
builder.Services.AddOpenApi();
builder.Services.AddDoSelectPersistence(builder.Configuration);
builder.Services.AddDoSelectIdempotency(builder.Configuration);
builder.Services.AddDoSelectFileStorage();
builder.Services.AddDoSelectSecurity(builder.Environment, builder.Configuration);
builder.Services.AddDoSelectRefunds();
builder.Services.AddDoSelectCatalogServices();
builder.Services.AddDoSelectShoppingServices();
builder.Services.AddDoSelectApplication();
builder.Services.AddDoSelectInvoicing();
builder.Services.AddSingleton<IEmailSender>(services =>
{
    var emailEnabled = builder.Configuration.GetValue<bool>("Features:EmailEnabled");
    return emailEnabled
        ? new SmtpEmailSender(services.GetRequiredService<IOptions<SmtpEmailOptions>>().Value)
        : new LocalEmailSender();
});
builder.Services.AddSingleton<EmailDispatchChannel>();
builder.Services.AddSingleton<IEmailDispatchQueue>(services => services.GetRequiredService<EmailDispatchChannel>());
builder.Services.AddHostedService<EmailDispatchBackgroundService>();
builder.Services.AddHostedService<UnverifiedMemberCleanupBackgroundService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSupportInfrastructure();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<IAdminSupportTicketService, AdminSupportTicketService>();
builder.Services.AddScoped<ISupportSlaQueueService, SupportSlaQueueService>();
builder.Services.AddScoped<ICaseWorkbenchService, CaseWorkbenchService>();
builder.Services.AddScoped<ISupportAttachmentReadService, SupportAttachmentReadService>();
builder.Services.AddScoped<ISupportAttachmentUploadService, SupportAttachmentUploadService>();

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

app.UseCors(SecurityServiceCollectionExtensions.FrontendCorsPolicy);

app.UseHttpsRedirection();

app.UseAuthentication();

// Cart routes accept both anonymous guests and authenticated members (no [Authorize]
// forces early authentication the way it does on every other controller), but the
// antiforgery filter still validates each unsafe request's token against
// HttpContext.User — ASP.NET Core's default token generator embeds the caller's
// identity, so a token minted for a signed-in member fails validation if User is
// still anonymous by the time the filter runs. This opportunistically authenticates
// the Member scheme (same call SecurityController.GetAntiforgeryToken already makes)
// before MVC's filter pipeline, without gating anonymous cart requests.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1/cart"))
    {
        var result = await context.AuthenticateAsync(DoSelectAuthenticationSchemes.Member);
        if (result.Succeeded && result.Principal is not null)
        {
            context.User = result.Principal;
        }
    }

    await next(context);
});

app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapObservabilityHealthChecks();

app.Run();

public partial class Program;
