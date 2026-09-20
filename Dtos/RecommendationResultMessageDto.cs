namespace RabbitHole.Vision.Worker.Dtos
{
    /// <summary>
    /// The recommendation result message dto.
    /// </summary>
    public class RecommendationResultMessageDto
    {
        /// <summary>
        /// Gets the extracted books.
        /// </summary>
        public required List<BookMessageDto> ExtractedBooks { get; init; }

        /// <summary>
        /// Gets the recommended books.
        /// </summary>
        public required List<BookMessageDto> RecommendedBooks { get; init; }

        /// <summary>
        /// Gets the error.
        /// </summary>
        public string? Error { get; init; }
    }
}