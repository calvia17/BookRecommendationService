using Microsoft.Data.SqlTypes;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The cached book details provider interface.
    /// </summary>
    public interface ICachedBookDetailsProvider
    {
        /// <summary>
        /// Gets the book details from cache.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The book details.</returns>
        Task<(CategorisedBook Book, SqlVector<float> Embedding)?> GetBookDetailsAsync(BookDetails book, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the book details in cache.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <param name="embedding">The embedding.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task SetBookDetailsAsync(CategorisedBook book, SqlVector<float> embedding, CancellationToken cancellationToken = default);
    }
}
