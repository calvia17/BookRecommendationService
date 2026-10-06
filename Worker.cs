using Azure.Messaging.ServiceBus;
using RabbitHole.Vision.Worker.Dtos;
using RabbitHole.Vision.Worker.Events;
using RabbitHole.Vision.Worker.Exceptions;
using RabbitHole.Vision.Worker.Services;
using System.Text.Json;

namespace RabbitHole.Vision.Worker
{
    /// <summary>
    /// The worker class.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ServiceBusProcessor processor;
        private readonly ServiceBusSender sender;
        private readonly IServiceProvider serviceProvider;

        /// <summary>
        /// Initializes the worker class.
        /// </summary>
        /// <param name="processor">The service bus processor.</param>
        /// <param name="sender">The service bus sender.</param>
        /// <param name="serviceProvider">The service provider.</param>
        public Worker(ServiceBusProcessor processor, ServiceBusSender sender, IServiceProvider serviceProvider)
        {
            this.processor = processor;
            this.sender = sender;
            this.serviceProvider = serviceProvider;
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

            // An uncaught exception means the message isn't completed and Service Bus will redeliver it (good for transient failures)
            try
            {
                using var scope = this.serviceProvider.CreateScope();
                var bookService = scope.ServiceProvider.GetRequiredService<IBookService>();
                var bookEvent = args.Message.Body.ToObjectFromJson<BookEvent>(options);
                if (bookEvent is BooksAddedEvent addedEvent)
                {
                    try
                    {
                        await bookService.AddBooksAsync(addedEvent.AddedBooks, args.CancellationToken);

                    }
                    catch (Exception ex) when (ex is ArgumentNullException || ex is DuplicateBookInputException || ex is BookAlreadyExistsException)
                    {
                        await args.DeadLetterMessageAsync(args.Message, "ProcessingError", ex.Message, args.CancellationToken);
                        return;
                    }
                }
                else if (bookEvent is BookDeletedEvent deletedEvent)
                {
                    try
                    {
                        await bookService.DeleteBookAsync(deletedEvent.BookIsbn, args.CancellationToken);

                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is BookNotFoundException)
                    {
                        await args.DeadLetterMessageAsync(args.Message, "ProcessingError", ex.Message, args.CancellationToken);
                        return;
                    }
                }
                else if (bookEvent is RecommendationsRequestedEvent recommendationsRequestEvent)
                {
                    try
                    {
                        var recommendations = await bookService.GetRecommendationsAsync(recommendationsRequestEvent.ImageBlobUrl, args.CancellationToken);
                        var response = new ServiceBusMessage(BinaryData.FromObjectAsJson(recommendations)) { CorrelationId = args.Message.CorrelationId };
                        await this.sender.SendMessageAsync(response, args.CancellationToken);

                    }
                    catch (Exception ex) when (ex is ArgumentException 
                        or JsonException 
                        or InvalidImageException 
                        or BookExtractionException 
                        or BookIdentificationException 
                        or EmbeddingCalculationException)
                    {
                        var recommendations = new RecommendationResultMessageDto
                        {
                            ExtractedBooks = [],
                            RecommendedBooks = [],
                            Error = ex.Message
                        };
                        var response = new ServiceBusMessage(BinaryData.FromObjectAsJson(recommendations)) { CorrelationId = args.Message.CorrelationId };
                        await this.sender.SendMessageAsync(response, args.CancellationToken);
                    }
                }
                else
                {
                    // Add to dead letter queue so it can be investigated later.
                    await args.DeadLetterMessageAsync(args.Message, "UnrecognizedEventType", cancellationToken: args.CancellationToken);
                    return;
                }

                // Processing completed successfully, so remove the message from the queue.
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            catch (JsonException ex)
            {
                await args.DeadLetterMessageAsync(args.Message, "DeserializationError", ex.Message, args.CancellationToken);
                return;
            }
        }

        private Task HandleErrorAsync(ProcessErrorEventArgs args)
        {
            // Log error here later.
            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // The DI container will dispose processor, sender, and client.
            await this.processor.StopProcessingAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}
