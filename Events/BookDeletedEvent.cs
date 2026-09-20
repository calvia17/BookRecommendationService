namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The book deleted event.
    /// </summary>
    public class BookDeletedEvent : BookEvent
    {
        /// <summary>
        /// Gets the book isbn.
        /// </summary>
        public string BookIsbn { get; }

        /// <summary>
        /// Initializes the book deleted event.
        /// </summary>
        /// <param name="bookIsbn">The book isbn.</param>
        public BookDeletedEvent(string bookIsbn)
        {
            this.BookIsbn = bookIsbn;
        }
    }
}
