using Gatekeeper.Web.Api;
using Gatekeeper.Web.Components;
using Gatekeeper.Web.Components.Account;
using Gatekeeper.Web.Data;
using Gatekeeper.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Use a factory so interactive Blazor components can create short-lived contexts per
// operation instead of sharing one context for the whole circuit.
builder.Services.AddDbContextFactory<GatekeeperDbContext>(options =>
    options.UseNpgsql(connectionString));
// ASP.NET Core Identity's stores need a scoped context; bridge it off the factory.
builder.Services.AddScoped<GatekeeperDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<GatekeeperDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<GatekeeperDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Gatekeeper application services.
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IPolicyEngine, PolicyEngine>();

var app = builder.Build();

// ADR-0051: say what this is, every time it boots.
Gatekeeper.Web.LabTargetBanner.Log(
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gatekeeper.LabTarget"),
    app.Environment.ContentRootPath);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// The decision API other applications call over HTTP.
app.MapPolicyApi();

// Apply migrations and seed demo data on startup.
await DbInitializer.InitializeAsync(app.Services);

app.Run();
