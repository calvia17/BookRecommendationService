using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<BookStoreContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var serviceBusConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
var processQueueName = builder.Configuration["ServiceBus:ProcessQueueName"];
var publishQueueName = builder.Configuration["ServiceBus:PublishQueueName"];

builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<ServiceBusClient>().CreateProcessor(processQueueName));
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<ServiceBusClient>().CreateSender(publishQueueName));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BookStoreContext>();
    await dbContext.Database.MigrateAsync();
}

host.Run();
