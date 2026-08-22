using RabbitHole.Vision.Worker.Objects;

namespace RabbitHole.Vision.Worker.Repositories
{
    /// <summary>
    /// The book repository interface.
    /// </summary>
    public interface IBookRepository
    {
        /// <summary>
        /// Gets all the books.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The books.</returns>
        Task<IEnumerable<Book>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the books by the isbns.
        /// </summary>
        /// <param name="isbns">The isbns.</param>
        /// <param name="trackChanges">A value indicating whether changes should be tracked.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The books.</returns>
        Task<IEnumerable<Book>> GetByIsbnsAsync(HashSet<string> isbns, bool trackChanges = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates multiple books.
        /// </summary>
        /// <param name="newBooksData">The new books data.</param>
        void AddMultiple(IEnumerable<Book> newBooksData);

        /// <summary>
        /// Saves the changes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task representing the save operation.</returns>
        Task SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the book.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        Task DeleteAsync(string isbn);
    }
}
