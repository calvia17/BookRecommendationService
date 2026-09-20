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
        public string Name { get; set; }

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
        /// <param name="embedding">The embedding.</param>
        public Book(string isbn, string name, SqlVector<float> embedding)
        {
            this.Isbn = isbn;
            this.Name = name;
            this.Embedding = embedding;
        }
    }
}
