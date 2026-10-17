using RabbitHole.Vision.Worker.Services;

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
        public List<BookDetails> AddedBooks { get; }

        /// <summary>
        /// Initializes the book added event.
        /// </summary>
        /// <param name="addedBooks">The added books.</param>
        public BooksAddedEvent(List<BookDetails> addedBooks)
        {
            this.AddedBooks = addedBooks;
        }
    }
}
