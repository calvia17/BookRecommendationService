namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The book not found exception.
    /// </summary>
    public class BookNotFoundException : BaseApplicationException
    {
        /// <summary>
        /// Gets the book isbns that were not found.
        /// </summary>
        public IEnumerable<string> BookIsbns { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BookNotFoundException" /> class.
        /// </summary>
        /// <param name="isbns">The isbns.</param>
        public BookNotFoundException(string isbn)
            : base($"The requested book with ISBN '{isbn}' was not found.")
        {
            this.BookIsbns = [isbn];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BookNotFoundException" /> class.
        /// </summary>
        /// <param name="isbns">The isbns.</param>
        public BookNotFoundException(IEnumerable<string> isbns)
            : base($"Some books were not found.")
        {
            this.BookIsbns = isbns;
        }
    }
}
