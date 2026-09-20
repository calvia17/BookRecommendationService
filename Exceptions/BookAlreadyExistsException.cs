namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The existing book record.
    /// </summary>
    /// <param name="Id">The book id.</param>
    /// <param name="Isbn">The isbn.</param>
    /// <param name="Title">The title.</param>
    public record ExistingBook(Guid Id, string Isbn, string Title);

    /// <summary>
    /// The book already exists exception.
    /// </summary>
    public class BookAlreadyExistsException : BaseApplicationException
    {
        /// <summary>
        /// Gets the existing books.
        /// </summary>
        public IEnumerable<ExistingBook> ExistingBooks { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BookAlreadyExistsException" /> class.
        /// </summary>
        /// <param name="existingBooks">The existing books.</param>
        public BookAlreadyExistsException(IEnumerable<ExistingBook> existingBooks)
            : base($"Some books already exist with the provided ISBNs.")
        {
            this.ExistingBooks = existingBooks;
        }
    }
}
