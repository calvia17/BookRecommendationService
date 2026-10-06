using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Google.Apis.Books.v1;
using Google.Apis.Services;
using Google.GenAI;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker;
using RabbitHole.Vision.Worker.Repositories;
using RabbitHole.Vision.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<BookStoreContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var serviceBusConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
var processQueueName = builder.Configuration["ServiceBus:ProcessQueueName"];
var publishQueueName = builder.Configuration["ServiceBus:PublishQueueName"];
var blobStorageConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
var shelfImageContainer = builder.Configuration["BlobStorage:ShelfImageContainer"];
var blobStorageHost = builder.Configuration["BlobStorage:StorageHost"];
var geminiKey = builder.Configuration["Gemini:ApiKey"];
var googleBooksKey = builder.Configuration["GoogleBooks:ApiKey"];

// Azure bus message queues
builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<ServiceBusClient>().CreateProcessor(processQueueName));
builder.Services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<ServiceBusClient>().CreateSender(publishQueueName));

// Azure blob storage
builder.Services.AddSingleton(serviceProvider =>
{
    var blobServiceClient = new BlobServiceClient(blobStorageConnectionString);
    return blobServiceClient.GetBlobContainerClient(shelfImageContainer);
});

// Gemini
builder.Services.AddSingleton(new Client(apiKey: geminiKey));

// Google Books
builder.Services.AddSingleton(new BooksService(new BaseClientService.Initializer
{
    ApiKey = googleBooksKey,
    ApplicationName = "BookRecommender"
}));

builder.Services.AddHostedService<Worker>();

builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IBlobStorageService>(sp => new BlobStorageService(sp.GetRequiredService<BlobContainerClient>(), blobStorageHost!));

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BookStoreContext>();
    await dbContext.Database.MigrateAsync();
}

host.Run();
