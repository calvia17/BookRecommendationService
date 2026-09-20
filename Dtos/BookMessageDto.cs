namespace RabbitHole.Vision.Worker.Dtos
{
    /// <summary>
    /// The book message dto.
    /// </summary>
    public class BookMessageDto
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
