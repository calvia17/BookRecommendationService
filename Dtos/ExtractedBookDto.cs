namespace RabbitHole.Vision.Worker.Dtos
{
    /// <summary>
    /// The extracted book dto.
    /// </summary>
    public class ExtractedBookDto
    {
        /// <summary>
        /// Gets the name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the author. This could be null if the author could not be extracted from the book.
        /// </summary>
        public string? Author { get; init; }
    }
}
