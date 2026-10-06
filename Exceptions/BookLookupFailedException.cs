namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The book lookup failed exception.
    /// </summary>
    public class BookLookupFailedException : BaseApplicationException
    {
        /// <summary>
        /// Gets the book isbn.
        /// </summary>
        public string? Isbn { get; }

        /// <summary>
        /// Gets the book name.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Gets the error message.
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// Initializes the book lookup failed exception.
        /// </summary>
        /// <param name="isbn">The book isbn.</param>
        /// <param name="name">The book name.</param>
        /// <param name="message">The error message.</param>
        public BookLookupFailedException(string? isbn, string? name, string message)
            : base($"Book lookup failed for book with {(isbn == null ? $"title : {name}" : $"ISBN: {isbn}")}. Error: {message}")
        {
            this.Isbn = isbn;
            this.Name = name;
            this.Error = message;
        }
    }
}
