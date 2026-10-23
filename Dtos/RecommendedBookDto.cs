namespace RabbitHole.Vision.Worker.Dtos
{
    /// <summary>
    /// The recommended book dto.
    /// </summary>
    public class RecommendedBookDto
    {
        /// <summary>
        /// Gets the isbn.
        /// </summary>
        public required string Isbn { get; init; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public required string Name { get; init; }
    }
}
