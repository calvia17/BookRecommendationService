using Microsoft.Data.SqlTypes;
using RabbitHole.Vision.Worker.Dtos;
using RabbitHole.Vision.Worker.Objects;
using static RabbitHole.Vision.Worker.Events.BooksAddedEvent;
using static RabbitHole.Vision.Worker.Events.BooksUpdatedEvent;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The book service interface.
    /// </summary>
    public interface IBookService
    {
        /// <summary>
        /// Adds the books.
        /// </summary>
        /// <param name="books">The books to add.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the add operation.</returns>
        Task AddBooksAsync(IEnumerable<BooksAddedData> books, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the books.
        /// </summary>
        /// <param name="books">The books to update.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the update operation.</returns>
        Task UpdateBooksAsync(IEnumerable<BooksUpdatedData> books, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the book.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the delete operation.</returns>
        Task DeleteBookAsync(string isbn, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets book recommendations.
        /// </summary>
        /// <param name="imageUrl">The image url.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The recommended books.</returns>
        Task<RecommendationResultMessageDto> GetRecommendationsAsync(string imageUrl, CancellationToken cancellationToken = default);
    }
}
