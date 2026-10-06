namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The embedding calculation exception.
    /// </summary>
    public class EmbeddingCalculationException : BaseApplicationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddingCalculationException" /> class.
        /// </summary>
        public EmbeddingCalculationException()
            : base("Failed to calculate embeddings for the books.")
        {
        }
    }
}
