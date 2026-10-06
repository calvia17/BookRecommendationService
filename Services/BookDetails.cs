namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The book details.
    /// </summary>
    public class BookDetails
    {
        public string? Isbn { get; }
        /// <summary>
        /// The title.
        /// </summary>
        public string? Title { get; }

        /// <summary>
        /// The author.
        /// </summary>
        public string? Author { get; }

        /// <summary>
        /// Initializes the extracted book details.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        /// <param name="title">The title.</param>
        /// <param name="author">The author.</param>
        public BookDetails(string? isbn, string? title, string? author)
        {
            this.Isbn = isbn;
            this.Title = title;
            this.Author = author;
        }
    }
}
