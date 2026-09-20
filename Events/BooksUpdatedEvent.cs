using RabbitHole.Vision.Worker.Objects;

namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The books updated event.
    /// </summary>
    public class BooksUpdatedEvent : BookEvent
    {
        /// <summary>
        /// The updated books.
        /// </summary>
        public List<BooksUpdatedData> UpdatedBooks { get; }

        /// <summary>
        /// The book record.
        /// </summary>
        /// <param name="Isbn">The isbn.</param>
        /// <param name="Name">The name.</param>
        /// <param name="Genres">The genres.</param>
        public record BooksUpdatedData(string Isbn, string Name, List<GenreType> Genres);

        /// <summary>
        /// Initializes the book updated event.
        /// </summary>
        /// <param name="updatedBooks">The updated books.</param>
        public BooksUpdatedEvent(List<BooksUpdatedData> updatedBooks)
        {
            this.UpdatedBooks = updatedBooks;
        }
    }
}
