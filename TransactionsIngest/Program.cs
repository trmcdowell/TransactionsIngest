using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TransactionsIngest.Data;
using TransactionsIngest.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<TransactionsDbContext>(options =>
    options.UseSqlite(builder.Configuration["ConnectionStrings:Default"]));

builder.Services.AddTransient<ITransactionFeed, MockTransactionFeed>();
builder.Services.AddTransient<IngestionService>();

var app = builder.Build();

using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
await context.Database.OpenConnectionAsync();
await context.Database.EnsureCreatedAsync();

var service = scope.ServiceProvider.GetRequiredService<IngestionService>();
await service.RunAsync();
