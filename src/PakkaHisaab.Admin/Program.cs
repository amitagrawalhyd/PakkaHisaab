using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Infrastructure.Auth;
using PakkaHisaab.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// ---------- services ----------
// Database:Provider = SqlServer (default; same Azure SQL DB as the API) | Sqlite (local dev).
var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(cs ?? "Data Source=pakkahisaab.admin.dev.db");
    else
        o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null));
});

// Cookie auth: only dbo.Users rows with IsAdmin = 1 can complete login (see Pages/Account/Login).
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        o.Cookie.Name = "PakkaHisaab.Admin.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:AutoCreate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Dev convenience only: first run against an empty local DB gets a ready-to-use admin login.
    if (app.Environment.IsDevelopment() && !db.Users.Any())
    {
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@pakkahisaab.local",
            DisplayName = "Dev Admin",
            PasswordHash = PasswordHasher.Hash("DevAdmin123!"),
            CreatedAtUtc = DateTime.UtcNow,
            IsAdmin = true
        });
        db.SaveChanges();
    }
}

// ---------- pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapRazorPages();

app.Run();
