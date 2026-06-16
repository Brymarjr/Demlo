using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
using Demlo.Infrastructure;
using Demlo.Infrastructure.Persistence;
using Demlo.Api.Filters;
using Demlo.Api.Middleware; 
using StackExchange.Redis; // ◄ 1. ADD THIS NAMESPACE FOR THE MULTIPLEXER SIGNATURES

var builder = WebApplication.CreateBuilder(args);

// Register your custom API-layer cryptographic filter
builder.Services.AddScoped<MonoWebhookVerificationFilter>();
builder.Services.AddScoped<PaystackWebhookVerificationFilter>();

// 1. Register Core MVC Controller Framework
builder.Services.AddControllers();

// 2. Register native .NET OpenApi services
builder.Services.AddOpenApi();

// 3. Configure Global API Versioning Framework (PL-31)
builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader(); // Extracts version from URL segment
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ──► 2. PL-83 CONNECTION MULTIPLEXER REGISTRATION HANDLER
// Connects natively to Memurai/Redis instance using your verified appsettings setup string
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisUrl = builder.Configuration.GetSection("RedisSettings:Url").Value ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(redisUrl);
});

// 4. Core Database Engine Registration
builder.Services.AddDbContext<DemloDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PostgresConnection"),
        b => b.MigrationsAssembly("Demlo.Infrastructure")
    )
);

// 5. Core Infrastructure Services Registration
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ──► PL-83: PLACE THE SECURITY SHIELD MIDDLEWARE RIGHT HERE
// It intercepts all incoming requests before they can ever map to controllers or consume DB channels
app.UseMiddleware<SecurityHardeningMiddleware>();

// 6. Map Controller Endpoints into the Application Routing Tree
app.MapControllers();

// Basic verification test endpoint to confirm API layer initialization
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", engine = ".NET 9/10 Infrastructure Ready" }));

app.Run();