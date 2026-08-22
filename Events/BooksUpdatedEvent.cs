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
        public IEnumerable<BooksUpdatedData> UpdatedBooks { get; }

        /// <summary>
        /// The book record.
        /// </summary>
        /// <param name="Id">The id.</param>
        /// <param name="Name">The name.</param>
        /// <param name="Genres">The genres.</param>
        public record BooksUpdatedData(Guid Id, string Name, IEnumerable<GenreType> Genres);

        /// <summary>
        /// Initializes the book updated event.
        /// </summary>
        /// <param name="updatedBooks">The updated books.</param>
        public BooksUpdatedEvent(IEnumerable<BooksUpdatedData> updatedBooks)
            : base(BookEventType.Updated)
        {
            this.UpdatedBooks = updatedBooks;
        }
    }
}
