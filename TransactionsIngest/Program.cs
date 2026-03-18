using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TransactionsIngest.Data;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var options = new DbContextOptionsBuilder<TransactionsDbContext>()
    .UseSqlite(config.GetConnectionString("Default"))
    .Options;

using var context = new TransactionsDbContext(options);
context.Database.EnsureCreated();
