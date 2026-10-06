namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The book extraction exception.
    /// </summary>
    public class BookExtractionException : BaseApplicationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BookExtractionException" /> class.
        /// </summary>
        public BookExtractionException()
            : base("Failed to extract books from the image.")
        {
        }
    }
}
