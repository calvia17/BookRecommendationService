using Azure.Messaging.ServiceBus;
using RabbitHole.Vision.Worker.Events;
using System.Text.Json;

namespace RabbitHole.Vision.Worker
{
    /// <summary>
    /// The worker class.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ServiceBusClient client;
        private readonly ServiceBusProcessor processor;
        private readonly ServiceBusSender sender;

        /// <summary>
        /// Initializes the worker class.
        /// </summary>
        /// <param name="client">The service bus client.</param>
        /// <param name="processor">The service bus processor.</param>
        /// <param name="sender">The service bus sender.</param>
        public Worker(ServiceBusClient client, ServiceBusProcessor processor, ServiceBusSender sender)
        {
            this.client = client;
            this.processor = processor;
            this.sender = sender;
            this.processor.ProcessMessageAsync += HandleMessageAsync;
            this.processor.ProcessErrorAsync += HandleErrorAsync;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Starts processing messages from the queue.
            await this.processor.StartProcessingAsync(stoppingToken);

            // Waits until the service is stopped.
            // This keeps the worker running and processing messages until the application is stopped.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessageAsync(ProcessMessageEventArgs args)
        {
            // This processes each message received from the queue.
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var bookEvent = args.Message.Body.ToObjectFromJson<BookEvent>(options);
            if (bookEvent is BooksAddedEvent addedEvent)
            {
                // Handle books added event
            }
            else if (bookEvent is BooksUpdatedEvent updatedEvent)
            {
                // Handle books updated event
            }
            else if (bookEvent is BookDeletedEvent deletedEvent)
            {
                // Handle book deleted event
            }
            else if (bookEvent is RecommendationsRequestedEvent recommendationsRequestEvent)
            {
                // Handle recommendations requested event
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }

        private Task HandleErrorAsync(ProcessErrorEventArgs args)
        {
            // Log error here later.
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.processor.StopProcessingAsync(cancellationToken);
            await this.processor.DisposeAsync();
            await this.sender.DisposeAsync();
            await this.client.DisposeAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}
