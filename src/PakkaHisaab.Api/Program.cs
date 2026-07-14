using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PakkaHisaab.Api.Auth;
using PakkaHisaab.Api.Data;
using PakkaHisaab.Api.Endpoints;
using PakkaHisaab.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- services ----------
// Database:Provider = SqlServer (default; Azure SQL free offer) | Sqlite (₹0 self-host/dev).
var dbProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(cs ?? "Data Source=/data/pakkahisaab.db");
    else
        o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key not configured"))),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Zero-touch first deploy: create the schema (tables, indexes, sequence) from the EF model.
// Equivalent to running db/001_schema.sql — pick ONE strategy; free-tier scripts use this.
if (app.Configuration.GetValue<bool>("Database:AutoCreate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

// ---------- pipeline ----------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapAccountEndpoints();
app.MapAiEndpoints();

app.Run();
