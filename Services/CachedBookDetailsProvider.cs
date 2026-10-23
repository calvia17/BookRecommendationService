using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The cached book details provider.
    /// </summary>
    public class CachedBookDetailsProvider : ICachedBookDetailsProvider
    {
        private const string embeddingVersion = "v1"; // This needs to be changed when the embedding prompt/tags change.
        private const string embeddingModel = "gemini-embedding-2";
        private const string embeddingDimensions = "768";
        private readonly IDistributedCache cache;

        private record CachedBookEntry(CategorisedBook Book, float[] Embedding);

        /// <summary>
        /// Initializes the cached book embeddings provider.
        /// </summary>
        /// <param name="cache">The distributed cache.</param>
        public CachedBookDetailsProvider(IDistributedCache cache)
        {
            this.cache = cache;
        }

        /// <summary>
        /// Gets the book details from cache.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The book details.</returns>
        public async Task<(CategorisedBook Book, SqlVector<float> Embedding)?> GetBookDetailsAsync(BookDetails book, CancellationToken cancellationToken = default)
        {
            if (book == null || string.IsNullOrWhiteSpace(book.Title) || string.IsNullOrWhiteSpace(book.Author))
            {
                return null;
            }

            var key = GetCacheKey(book.Title, book.Author);
            var bytes = await this.cache.GetAsync(key, cancellationToken);
            if (bytes == null)
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<CachedBookEntry>(bytes);
            if (result == null)
            {
                return null;
            }

            return (result.Book, new SqlVector<float>(result.Embedding));
        }

        /// <summary>
        /// Sets the book details in cache.
        /// </summary>
        /// <param name="title">The title of the book (used to set the cache key).</param>
        /// <param name="author">The author of the book (used to set the cache key).</param>
        /// <param name="book">The book.</param>
        /// <param name="embedding">The embedding.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task SetBookDetailsAsync(string? title, string? author, CategorisedBook book, SqlVector<float> embedding, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var key = GetCacheKey(title, author ?? string.Empty);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new CachedBookEntry(book, embedding.Memory.Span.ToArray()));
            await this.cache.SetAsync(
                key,
                bytes,
                new DistributedCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromDays(14),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(90)
                },
                cancellationToken);
        }

        private static string GetCacheKey(string title, string author)
        {
            var sanitisedTitle = Regex.Replace(title, @"\s+", " ").Trim().ToLowerInvariant();
            var sanitisedAuthor = Regex.Replace(author, @"\s+", " ").Trim().ToLowerInvariant();
            var key = $"{embeddingVersion}|{embeddingModel}|{embeddingDimensions}|{sanitisedTitle}|{sanitisedAuthor}";
            return key;
        }
    }
}
