using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Demlo.Infrastructure;
using Demlo.Infrastructure.Persistence;
using Demlo.Api.Filters;
using Demlo.Api.Middleware; 
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Register your custom API-layer cryptographic filter
builder.Services.AddScoped<MonoWebhookVerificationFilter>();
builder.Services.AddScoped<PaystackWebhookVerificationFilter>();

// 1. Register Core MVC Controller Framework
builder.Services.AddControllers();

// ──► PL-91: JWT BEARER AUTHENTICATION ENGINE (THE FIX)
var jwtSecret = builder.Configuration["JwtSettings:Secret"] 
    ?? throw new InvalidOperationException("Cryptographic JWT Token Signing Key is unconfigured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

builder.Services.AddAuthorization();

// 2. Register native .NET OpenApi services
builder.Services.AddOpenApi();

// 3. Configure Global API Versioning Framework (PL-31)
builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// 4. PL-83 CONNECTION MULTIPLEXER REGISTRATION HANDLER
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisUrl = builder.Configuration.GetSection("RedisSettings:Url").Value ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(redisUrl);
});

// 5. Core Database Engine Registration
builder.Services.AddDbContext<DemloDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PostgresConnection"),
        b => b.MigrationsAssembly("Demlo.Infrastructure")
    )
);

// 6. Core Infrastructure Services Registration
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ──► PIPELINE ORDER MATTERS: Authentication MUST come before Authorization
app.UseAuthentication(); 
app.UseAuthorization();

// 7. PL-83: SECURITY SHIELD MIDDLEWARE
app.UseMiddleware<SecurityHardeningMiddleware>();

// 8. Map Controller Endpoints into the Application Routing Tree
app.MapControllers();

// Basic verification test endpoint to confirm API layer initialization
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", engine = ".NET 9/10 Infrastructure Ready" }));

app.Run();