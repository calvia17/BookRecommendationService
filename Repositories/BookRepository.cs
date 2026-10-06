using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Services;
using System.Numerics.Tensors;

namespace RabbitHole.Vision.Worker.Repositories
{
    /// <summary>
    /// The book repository.
    /// </summary>
    public class BookRepository : IBookRepository
    {
        private readonly BookStoreContext context;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookRepository" /> class.
        /// </summary>
        /// <param name="context">The context.</param>
        public BookRepository(BookStoreContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// Gets all the books.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The books.</returns>
        public async Task<IEnumerable<Book>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var books = await this.context.Books
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);
            return books;
        }

        /// <summary>
        /// Gets the books by the isbns.
        /// </summary>
        /// <param name="isbns">The isbns.</param>
        /// <param name="trackChanges">A value indicating whether changes should be tracked.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The books.</returns>
        public async Task<IEnumerable<Book>> GetByIsbnsAsync(HashSet<string> isbns, bool trackChanges = false, CancellationToken cancellationToken = default)
        {
            IQueryable<Book> booksQuery = this.context.Books;
            if (!trackChanges)
            {
                booksQuery = booksQuery.AsNoTracking();
            }

            var books = await booksQuery
                        .Where(b => isbns.Contains(b.Isbn))
                        .ToListAsync(cancellationToken);
            return books;
        }

        /// <summary>
        /// Creates multiple books.
        /// </summary>
        /// <param name="newBooksData">The new books data.</param>
        public void AddMultiple(IEnumerable<Book> newBooksData)
        {
            ArgumentNullException.ThrowIfNull(newBooksData);
            this.context.Books.AddRange(newBooksData);
        }

        /// <summary>
        /// Deletes the book.
        /// </summary>
        /// <param name="book">The book to delete.</param>
        public async void Delete(Book book)
        {
            ArgumentNullException.ThrowIfNull(book);
            this.context.Books.Remove(book);
        }

        /// <summary>
        /// Gets the recommended books based on the provided book embeddings.
        /// </summary>
        /// <param name="bookEmbeddings">The book embeddings.</param>
        /// <param name="count">The number of recommended books to return.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The recommended books.</returns>
        public async Task<List<Book>> GetRecommendedBooksAsync(List<(BookDetails Book, SqlVector<float> Vector)> bookEmbeddings, int count, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(bookEmbeddings);

            if (bookEmbeddings.Count == 0)
            {
                return [];
            }

            // Each embedding can be represented as a vector in high-dimensional space.
            // The length of these vectors does not give us any information about the meaning of the text.
            // The direction of the vector is what determines if 2 vectors are similar.
            // The smaller the angle between 2 vectors, the more similar they are.
            // Cosine similarity measures the cosine of the angle between 2 vectors
            // without considering their magnitudes. This tells us if 2 books are similar.

            // Minimum approach: For each input book embedding, we calculate the cosine distance to all books in the database.
            // Then we take the minimum distance for each stored book to order them.
            // Advantage: It ensures we never recommend a book vector that is too far away from any of the input book vectors.
            // Disadvantage: It does not consider the overall taste profile of the user.
            // If the user has 5 fantasy books and one mystery book, the minimum approach would give equal weight to the mystery and fantasy books in producing the recommendations.

            // Centroid approach: We calculate the centroid of the input book embeddings and then calculate the cosine distance between the centroid and all books in the database.
            // Advantage: It considers the overall taste profile of the user. If there are 5 fantasy books and one mystery book, the centroid will be closer to the fantasy books and will recommend more fantasy books.
            // Disadvantage: If the input vectors include equal fantasy and mystery books, the centroid will be in the middle of the 2 genres and will recommend books that are not similar to either genre.

            // Hybrid approach: We calculate first using the min approach and take a larger set of books.
            // This ensures that we do not recommend books that are too far away from any of the input book vectors.
            // Then we calculate the centroid of the input book embeddings and calculate the cosine distance between the centroid and the set of books from the min approach.
            // When considering how many books to take from the min approach, we need to consider 2 things:
            // We should take enough books so that the centroid approach has enough books to choose from.
            // But we should not take too many books that the result contains books that are too far away from the input book vectors.

            // Min approach:
            var books = new List<(Book Book, double Distance)>(); 
            var inputIsbns = bookEmbeddings.Select(be => be.Book.Isbn).ToHashSet();
            foreach (var bookEmbedding in bookEmbeddings)
            {
                // For each input book embedding, we calculate the distance to each book in the database.
                var distances = await this.context.Books
                                    .AsNoTracking()
                                    .Where(book => !inputIsbns.Contains(book.Isbn))
                                    .Select(book => new { Book = book, Distance = EF.Functions.VectorDistance("cosine", book.Embedding, bookEmbedding.Vector) })
                                    .OrderBy(book => book.Distance)
                                    .Take(count * 3)
                                    .ToListAsync(cancellationToken);
                books.AddRange(distances.Select(d => (d.Book, d.Distance)));
            }

            // Group by book and take the minimum distance for each book.
            var filteredBooks = books
                                    .GroupBy(book => book.Book.Isbn)
                                    .Select(group => new { group.First().Book, MinDistance = group.Min(b => b.Distance) })
                                    .OrderBy(book => book.MinDistance)
                                    .Take(count * 3)
                                    .Select(b => (b.Book, b.MinDistance))
                                    .ToList();

            // Centroid approach:
            var embeddingLength = bookEmbeddings.First().Vector.Length;
            var centroid = new float[embeddingLength];
            foreach (var embedding in bookEmbeddings)
            {
                var embeddingSpan = embedding.Vector.Memory.Span;
                for (var i = 0; i < embeddingLength; i++)
                {
                    centroid[i] += embeddingSpan[i];
                }
            }

            for (var i = 0; i < embeddingLength; i++)
            {
                centroid[i] /= bookEmbeddings.Count;
            }

            var recommendedBooks = filteredBooks
                                    .Select(book =>
                                    {
                                        // Calculates the cosine similarity between the book's embedding and the centroid vector.
                                        // Cosine similarity is just the cosine of the angle between the two vectors.
                                        // We can manually compute this using the formula: cosine similarity = a.b / (|a| * |b|).
                                        // But Tensor Primitives uses SIMD to compute this which makes the CPU compute it faster.
                                        var cosineSimilarity = TensorPrimitives.CosineSimilarity(book.Book.Embedding.Memory.Span, centroid);

                                        // Cosine distance = 1 - cosine similarity.
                                        // This is a measure of how close the 2 vectors are.
                                        // The smaller the angle between the 2 vectors, the larger the cosine similarity.
                                        // So we calculate the cosine distance such that the smaller the distance, the more similar the 2 vectors are.
                                        var centroidDistance = 1 - cosineSimilarity;

                                        // We can tune the weights depending on how much we want to weight the minimum distance vs the centroid distance.
                                        var hybridDistance = 0.4 * book.MinDistance + 0.6 * centroidDistance;
                                        return (book.Book, hybridDistance);
                                    })
                                    .OrderBy(book => book.hybridDistance)
                                    .Take(count)
                                    .Select(b => b.Book)
                                    .ToList();

            return recommendedBooks;
        }

        /// <summary>
        /// Saves the changes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task representing the save operation.</returns>
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await this.context.SaveChangesAsync(cancellationToken);
        }
    }
}
