using RabbitHole.Vision.Worker.Services;

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
        public List<BookDetails> UpdatedBooks { get; }

        /// <summary>
        /// Initializes the book updated event.
        /// </summary>
        /// <param name="updatedBooks">The updated books.</param>
        public BooksUpdatedEvent(List<BookDetails> updatedBooks)
        {
            this.UpdatedBooks = updatedBooks;
        }
    }
}
