using Microsoft.Data.SqlTypes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RabbitHole.Vision.Worker.Objects
{
    /// <summary>
    /// The book class.
    /// </summary>
    public class Book
    {
        /// <summary>
        /// Gets the id.
        /// </summary>
        [Key]
        public Guid Id { get; }

        /// <summary>
        /// Gets the isbn.
        /// </summary>
        public string Isbn { get; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the author.
        /// </summary>
        public string Author { get; }

        /// <summary>
        /// Gets or sets the type of the book.
        /// </summary>
        public BookType BookType { get; }

        /// <summary>
        /// Gets or sets the genre group.
        /// </summary>
        public GenreGroup GenreGroup { get; }

        /// <summary>
        /// Gets the embedding.
        /// </summary>
        [Column(TypeName = "vector(768)")] // Smaller vector for faster similarity searches and less storage
        public SqlVector<float> Embedding { get; set; }

        /// <summary>
        /// Initializes the book.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <param name="isbn">The isbn.</param>
        /// <param name="name">The name.</param>
        /// <param name="author">The author.</param>
        /// <param name="bookType">The type of the book.</param>
        /// <param name="genreGroup">The genre group.</param>
        /// <param name="embedding">The embedding.</param>
        public Book(string isbn, string name, string author, BookType bookType, GenreGroup genreGroup, SqlVector<float> embedding)
        {
            this.Isbn = isbn;
            this.Name = name;
            this.Author = author;
            this.BookType = bookType;
            this.GenreGroup = genreGroup;
            this.Embedding = embedding;
        }
    }
}
