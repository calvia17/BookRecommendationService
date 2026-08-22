using RabbitHole.Vision.Worker.Objects;

namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The books added event.
    /// </summary>
    public class BooksAddedEvent : BookEvent
    {
        /// <summary>
        /// The added books.
        /// </summary>
        public IEnumerable<BooksAddedData> AddedBooks { get; }

        /// <summary>
        /// The book record.
        /// </summary>
        /// <param name="Id">The id.</param>
        /// <param name="Isbn">The isbn.</param>
        /// <param name="Name">The name.</param>
        /// <param name="Genres">The genres.</param>
        public record BooksAddedData(Guid Id, string Isbn, string Name, IEnumerable<GenreType> Genres);

        /// <summary>
        /// Initializes the book added event.
        /// </summary>
        /// <param name="addedBooks">The added books.</param>
        public BooksAddedEvent(IEnumerable<BooksAddedData> addedBooks)
            : base(BookEventType.Added)
        {
            this.AddedBooks = addedBooks;
        }
    }
}
