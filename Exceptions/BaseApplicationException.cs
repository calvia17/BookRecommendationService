namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The base application exception.
    /// </summary>
    public class BaseApplicationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseApplicationException" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        protected BaseApplicationException(string message) : base(message) { }
    }
}
