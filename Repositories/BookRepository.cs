using Google.Apis.Books.v1.Data;
using Google.Apis.Util;
using Google.GenAI;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.ConstrainedExecution;

namespace RabbitHole.Vision.Worker.Repositories
{
    /// <summary>
    /// The book repository.
    /// </summary>
    public class BookRepository : IBookRepository
    {
        private readonly BookStoreContext context;
        private readonly IDbContextFactory<BookStoreContext> contextFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookRepository" /> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="contextFactory">The context factory.</param>
        public BookRepository(BookStoreContext context, IDbContextFactory<BookStoreContext> contextFactory)
        {
            this.context = context;
            this.contextFactory = contextFactory;
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
        /// <param name="numRecommendations">The number of recommended books to return.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The recommended books.</returns>
        public async Task<List<Book>> GetRecommendedBooksAsync(List<(CategorisedBook Book, SqlVector<float> Vector)> bookEmbeddings, int numRecommendations, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(bookEmbeddings);

            if (bookEmbeddings.Count == 0)
            {
                return [];
            }

            bookEmbeddings = bookEmbeddings.DistinctBy(x => x.Book.Title).ToList();
            var initialCluster = new Cluster(bookEmbeddings[0].Vector.Memory.Span.ToArray(), bookEmbeddings[0].Book);
            var clusters = new List<Cluster>() { initialCluster };

            // Use a temporary vector to store intermediate vector results to avoid creating new 768 dimensional vectors in each iteration
            // which can be expensive in terms of memory allocation and garbage collection.
            var intermediateVector = new float[clusters[0].Centroid.Length];
            for (var i = 1; i < bookEmbeddings.Count; i++)
            {
                // Use standard for loop here instead of linq or foreach since it will be much faster.
                // Each embedding has 768 dimensions. So the difference in performance will be significant especially when the number of books in the shelf is large.
                var minDistance = float.MaxValue;
                Cluster? closestCluster = null;
                for (var j = 0; j < clusters.Count; j++)
                {
                    var bookA = bookEmbeddings[i].Book.Title;
                    var clusterBooks = string.Join(',', clusters[j].Books);
                    var distance = 1 - TensorPrimitives.CosineSimilarity(clusters[j].Centroid, bookEmbeddings[i].Vector.Memory.Span);

                    // We need to filter by book type and broad genre group to ensure that books across genre groups/different book types are not clustered together
                    // even if they have similar themes. The embedding vectors can be similar for books with different genre/book type if they have similar themes so we need to filter them.
                    // The recommendations and clustering need to identify themes within the same genre group and book type only.
                    if (distance < minDistance
                        && clusters[j].BookType == bookEmbeddings[i].Book.BookType 
                        && clusters[j].GenreGroup == bookEmbeddings[i].Book.GenreGroup)
                    {
                        closestCluster = clusters[j];
                        minDistance = distance;
                    }
                }

                // Threshold distance = 0.25 (cosine similarity = 0.75, angle = 41.41 degrees)
                // Reason for threshold choice: Higher thresholds cause genre bleeding (Example: Contemporary Romance books merge with Fantasy Romance)
                // Lower thresholds dont cluster well and books that are very similar dont get clustered together.
                // For best quality, we keep it at 0.25 which splits the different groups well.
                if (closestCluster != null && minDistance < 0.25)
                {
                    // Update the centroid to include the new book in the cluster.
                    var newSize = closestCluster.Size + 1;
                    closestCluster.Size = newSize;
                    closestCluster.Books.Add(bookEmbeddings[i].Book);
                }
                else
                {
                    var newCluster = new Cluster(bookEmbeddings[i].Vector.Memory.Span.ToArray(), bookEmbeddings[i].Book);
                    clusters.Add(newCluster);
                }
            }

            // Sort clusters by size and take the top 20 clusters.
            // This ensures that we are only considering the most significant clusters for recommendations.
            clusters = clusters.OrderByDescending(cluster => cluster.Size).Take(20).ToList();

            // Fetch books from the database for each cluster.
            var books = new ConcurrentBag<(Book Book, double CentroidDistance)>();
            var inputBooks = bookEmbeddings.Select(be => $"{be.Book.Title}, {be.Book.Author}").ToHashSet();
            var filteredBookCount = clusters.Sum(cluster => cluster.Size);

            // TODO: Add retries here.
            await Parallel.ForEachAsync(
                clusters,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = cancellationToken
                },
                async (cluster, cancellationToken) =>
                {
                    // Create a new database context for each parallel task because the context is not thread safe and cannot be shared across threads.
                    await using var dbContext = await this.contextFactory.CreateDbContextAsync(cancellationToken);

                    // Multiple with 2 to get a buffer incase same book gets added for multiple clusters because that would result in a shorter recommendation list when duplicates are removed.
                    var slots = (int)Math.Ceiling((((float)cluster.Size) / filteredBookCount) * numRecommendations * 2); // This can be tuned.

                    // For each input book embedding, we calculate the distance to each book in the database.
                    var clusterCentroid = new SqlVector<float>(cluster.Centroid);
                    var distances = await dbContext.Books
                                        .AsNoTracking()
                                        .Where(book => !inputBooks.Contains(book.Name + ", " + book.Author) && book.BookType == cluster.BookType && book.GenreGroup == cluster.GenreGroup)
                                        .Select(book => new { Book = book, CentroidDistance = EF.Functions.VectorDistance("cosine", book.Embedding, clusterCentroid) })
                                        .OrderBy(book => book.CentroidDistance)
                                        .Take(slots)
                                        .ToListAsync(cancellationToken);
                    foreach (var distance in distances)
                    {
                        books.Add((distance.Book, distance.CentroidDistance));
                    }
                }
            );

            // Group by book and take the nearest centroid distance for each book.
            var recommendedBooks = books
                                    .GroupBy(book => book.Book.Isbn)
                                    .Select(group => new { group.First().Book, NearestCentroidDistance = group.Min(b => b.CentroidDistance) })
                                    .OrderBy(book => book.NearestCentroidDistance)
                                    .Take(numRecommendations) 
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
