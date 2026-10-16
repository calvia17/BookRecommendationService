using RabbitHole.Vision.Worker.Objects;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The classified shelf book.
    /// </summary>
    public class CategorisedBook : UncategorisedBook
    {
        /// <summary>
        /// Gets the book type.
        /// </summary>
        public BookType BookType { get; }

        /// <summary>
        /// Gets the genre group.
        /// </summary>
        public GenreGroup GenreGroup { get; }

        /// <summary>
        /// Gets the book tags.
        /// </summary>
        public IEnumerable<string> BookTags { get; }

        /// <summary>
        /// Initializes the categorised book.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        /// <param name="title">The title.</param>
        /// <param name="author">The author.</param>
        /// <param name="bookType">The book type.</param>
        /// <param name="genreGroup">The genre group.</param>
        /// <param name="bookTags">The book tags.</param>
        public CategorisedBook(string? isbn, string? title, string? author, BookType bookType, GenreGroup genreGroup, IEnumerable<string> bookTags)
            : base(isbn, title, author)
        {
            this.BookType = bookType;
            this.GenreGroup = genreGroup;
            this.BookTags = bookTags;
        }
    }
}
