namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The duplicate book input exception.
    /// </summary>
    public class DuplicateBookInputException : BaseApplicationException
    {
        /// <summary>
        /// Gets the duplicate isbns.
        /// </summary>
        public IEnumerable<string>? DuplicateIsbns { get; }

        /// <summary>
        /// Gets the duplicate IDs.
        /// </summary>
        public IEnumerable<Guid>? DuplicateIds { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DuplicateBookInputException" /> class.
        /// </summary>
        /// <param name="duplicateIsbns">The duplicate ISBNs.</param>
        public DuplicateBookInputException(IEnumerable<string> duplicateIsbns)
            : base($"Duplicate ISBNS found in the input.")
        {
            this.DuplicateIsbns = duplicateIsbns;
        }
    }
}
