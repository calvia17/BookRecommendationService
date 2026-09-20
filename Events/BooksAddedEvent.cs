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
        public List<BooksAddedData> AddedBooks { get; }

        /// <summary>
        /// The book record.
        /// </summary>
        /// <param name="Isbn">The isbn.</param>
        /// <param name="Name">The name.</param>
        /// <param name="Genres">The genres.</param>
        public record BooksAddedData(string Isbn, string Name, List<GenreType> Genres);

        /// <summary>
        /// Initializes the book added event.
        /// </summary>
        /// <param name="addedBooks">The added books.</param>
        public BooksAddedEvent(List<BooksAddedData> addedBooks)
        {
            this.AddedBooks = addedBooks;
        }
    }
}
