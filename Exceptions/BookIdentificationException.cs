namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The book identification exception.
    /// </summary>
    public class BookIdentificationException : BaseApplicationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BookIdentificationException" /> class.
        /// </summary>
        public BookIdentificationException()
            : base("Failed to identify the books.")
        {
        }
    }
}
