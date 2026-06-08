using Microsoft.EntityFrameworkCore;
using PeerLend.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// --- Core Database Engine Registration ---
builder.Services.AddDbContext<PeerLendDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PostgresConnection"),
        b => b.MigrationsAssembly("PeerLend.Infrastructure")
    )
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Basic verification test endpoint to confirm API layer initialization
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", engine = ".NET 9/10 Infrastructure Ready" }));

app.Run();