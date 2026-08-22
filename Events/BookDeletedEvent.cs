namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The book deleted event.
    /// </summary>
    public class BookDeletedEvent : BookEvent
    {
        /// <summary>
        /// Gets the book id.
        /// </summary>
        public Guid BookId { get; }

        /// <summary>
        /// Initializes the book deleted event.
        /// </summary>
        /// <param name="bookId">The book id.</param>
        public BookDeletedEvent(Guid bookId)
            : base(BookEventType.Deleted)
        {
            this.BookId = bookId;
        }
    }
}
