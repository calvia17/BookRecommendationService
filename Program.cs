using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker;

var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();

host.Run();