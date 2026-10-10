using Google.Apis.Books.v1.Data;
using Google.GenAI;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Services;
using System.Collections.Concurrent;
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
        public async Task<List<Book>> GetRecommendedBooksAsync(List<(BookDetails Book, SqlVector<float> Vector)> bookEmbeddings, int numRecommendations, CancellationToken cancellationToken = default)
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
            // Cosine similarity is the cosine of the angle between 2 vectors. This tells us if 2 books are similar.
            // The smaller the cosine similarity (cos of the angle), the less similar the 2 vectors are.
            // Cosine distance is 1 - cosine similarity. The smaller the cosine distance, the more similar the 2 vectors are.
            // Max cosine distance = 2 (when angle is 180 degrees, cosine of the angle = -1, so cosine distance = 1 - (-1) = 2)
            // Angle of 90 degrees means the 2 vectors are completely unrelated since they share no direction.

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

            // CLUSTERING: 
            // We can improve this further by creating clusters of books in the database and calculating the centroid of each cluster.
            // This reduces the number of database calls from the number of input book embeddings to the number of clusters.
            // It also improves the accuracy of the centroid approach since we would be using the centroid of each cluster rather than the centroid of the entire bookshelf which can lead to a centroid that is not representative of the user's taste profile.
            // Clustering improves performance but can affect accuracy if the clusters have distinct books that cause cluster centroids to point to books that are very different from the books in the cluster.
            // So we need to choose a clustering approach that balances performance and accuracy.
            // For each cluster, we allocate slots = (number of books in the cluster / total number of books in the shelf) *count
            // where count is the number of recommended books to return.
            // This ensures that each cluster gets the right share in the recommendation list and correctly represents the user's taste profile.
            // If a cluster has more books it means that the user will prefer books of that type more and so that cluster should get more slots in the recommendation list.

            // Clustering approaches:
            // K-means algorithm: We could create clusters using the K-means clustering algorithm: We randomly select k books from the input to be the centroids of the clusters.
            // For each input book embedding, we calculate the distance to each centroid and assign the book to the cluster with the closest centroid.
            // The issue with this approach is that the accuracy depends on k.
            // If k is too small and the books are very distinct, the cluster centroid can point to very different books than those in the cluster.
            // Even if k is large and all the books are distinct, the cluster centroid can point to books that are not in the cluster.
            // So k means clustering can improve performance by reducing the number of database calls to the cluster size k, but it reduces accuracy.

            // Sequential Leader-Follower Algorithm with Running Centroid Updates: Creating clusters using a running average approach ensures that the centroid of the cluster is always representative of the books in the cluster
            // as long as the threshold for the distance centroid to the new book is kept small enough.
            // If the books are very distinct we get clusters with very less or even just 1 book. It reduces performance in this case but ensures accuracy.
            // If the books are very similar or there are very similar groups, it reduces the number of database calls while preserving accuracy.
            // It's the best of both worlds - performance and accuracy.
            // It will also represent taste profiles since we can choose m books representative of each cluster where m = (number of books in the cluster / total number of books in the shelf) * number of books to recommend.
            // This is also better than using a global centroid after the min approach because if the input books have groups of similar size, 
            // the global centroid will pick books that are median to both groups even when there are books that are very similar to the input books in both groups.
            // This will result in mediocre recommendations. We need to give good recommendations.
            // At each step in this algorithm, we find the closest cluster centroid under a threshold distance to the new book and add the book to that cluster.
            // If there is no cluster centroid under the threshold distance, we create a new cluster with the new book as the centroid.

            // Cluster limiting and pruning:
            // While performance is important and k means can improve performance more than the running average centroid approach, we should not sacrifice accuracy for performance.
            // The running average centroid approach will boost performance when it is possible to do so without sacrificing accuracy.
            // It ensures we give the best recommendations.
            // The only issue with this is that if the user uploads a very large shelf which is very distinct, there can be a huge number of clusters.
            // This can result in high number of database calls significantly reducing performance.
            // What we can do in this case is, if the number of clusters is greater than a threshold clusterLimit, we can remove the clusters with the least number of books until we are below the threshold.

            // Performance enhancements of this method:
            // 1. Parallel processing
            // 2. SIMD calculations
            // 3. Cluster limiting and pruning


            // Creating clusters using Sequential Leader-Follower Algorithm with Running Centroid Updates:
            // Need to test and tune the threshold distance with test cases like:
            // Direct Sequels / Same Series (Target: Group Together)
            //      Harry Potter 1 vs.Harry Potter 2
            //      Dune vs. Dune Messiah
            // Same Sub-Genre / Identical Tropes(Target: Group Together)
            //      Twilight vs. The Vampire Diaries
            //      The Hunger Games vs.Divergent

            // Different Sub-Genres / Cross - Genre(Target: Separate)
            //      Twilight vs. Dracula(Classic Gothic Horror vs.YA Vampire Romance)
            //      Dune vs. Neuromancer(Space Opera vs.Cyberpunk)
            var clusters = new List<Cluster>() { new Cluster(bookEmbeddings[0].Vector.Memory.Span.ToArray(), 1) };

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
                    var distance = 1 - TensorPrimitives.CosineSimilarity(clusters[j].Centroid, bookEmbeddings[i].Vector.Memory.Span);
                    if (distance < minDistance)
                    {
                        closestCluster = clusters[j];
                        minDistance = distance;
                    }
                }

                // Threshold distance = 0.2 (cosine similarity = 0.8, angle = 36.9 degrees)
                if (closestCluster != null && minDistance < 0.2) // Might need to increase this.
                {
                    // Update the centroid to include the new book in the cluster.
                    var newSize = closestCluster.Size + 1;
                    TensorPrimitives.Subtract(bookEmbeddings[i].Vector.Memory.Span, closestCluster.Centroid, intermediateVector);
                    TensorPrimitives.Divide(intermediateVector, newSize, intermediateVector);
                    TensorPrimitives.Add(closestCluster.Centroid, intermediateVector, closestCluster.Centroid);
                    closestCluster.Size = newSize;
                }
                else
                {
                    clusters.Add(new Cluster(bookEmbeddings[i].Vector.Memory.Span.ToArray(), 1));
                }
            }

            // Cluster limiting and pruning:
            if (clusters.Count > 20)
            {
                // Remove the clusters with the least number of books.
                clusters = clusters.OrderByDescending(c => c.Size).Take(20).ToList();
            }


            // Fetch books from the database for each cluster.
            var books = new ConcurrentBag<(Book Book, double CentroidDistance)>();
            var inputIsbns = bookEmbeddings.Select(be => be.Book.Isbn).ToHashSet();
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
                                        .Where(book => !inputIsbns.Contains(book.Isbn))
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
