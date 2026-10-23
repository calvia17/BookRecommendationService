using Google;
using Google.Apis.Books.v1;
using Google.Apis.Books.v1.Data;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Data.SqlTypes;
using RabbitHole.Vision.Worker.Dtos;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Repositories;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Type = Google.GenAI.Types.Type;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The book service.
    /// </summary>
    public class BookService : IBookService
    {
        private const int batchSize = 25;
        private const string ocrExtractionPrompt = """
            Extract the following details for each book from the book spines in the book shelf image.
            - title: the title of the book (extract from the book spine)
            - author: the author of the book (extract from the book spine)
            - bookType: Choose the type of the book from the provided list.
            - genreGroup: Choose the genre group of the book from the provided list. If there are multiple genre groups, choose the one that describes the book the best.
            - Use genreGroup 'Romance' if the romantic relationship is the main plotline, or the book is marketed as romance or romantasy. This applies to any setting (fantasy, historical, contemporary, etc.). Check this first.
            - Use genreGroup 'ScienceFictionFantasy' if the book is science fiction or fantasy where romance is a side element or absent.
            - Avoid using Unknown or OtherFiction unless it is absolutely not possible to classify into any of the other categories. 
            - tags: up to 3 short lowercase tags (1-3 words each) describing the
              book's genre, subgenre, themes and main tropes, most defining first (using your knowledge of the book).
              Use common reader-recognized terms, for example "romantasy",
              "enemies to lovers", "vampires", "domestic thriller", "grief".
              Do not repeat the same idea in different words.
            """;
        private const string bookClassificationPrompt = """
            You will receive a list of books. Each has an isbn, title and author.
            Classify each book by populating the following fields:
            - isbn: copied exactly from the input.
            - title: copied exactly from the input.
            - author: copied exactly from the input.
            - bookType: the type of the book (from the provided BookType enum options)
            - genreGroup: the genre group of the book (from the provided GenreGroup enum options)
            - Use genreGroup 'Romance' if the romantic relationship is the main plotline, or the book is marketed as romance or romantasy. This applies to any setting (fantasy, historical, contemporary, etc.). Check this first.
            - Use genreGroup 'ScienceFictionFantasy' if the book is science fiction or fantasy where romance is a side element or absent.
            - Avoid using Unknown or OtherFiction unless it is absolutely not possible to classify into any of the other categories. 
            - tags: up to 3 short lowercase tags (1-3 words each) describing the
              book's genre, subgenre, themes and main tropes, most defining first.
              Use common reader-recognized terms, for example "romantasy",
              "enemies to lovers", "vampires", "domestic thriller", "grief".
              Do not repeat the same idea in different words.
            If you cannot identify and tag the books, leave the tags as unknown. Do not make up information.
            """;

        private readonly IBookRepository bookRepository;
        private readonly IBlobStorageService blobStorageService;
        private readonly ICachedBookDetailsProvider cachedBookEmbeddingsProvider;
        private readonly Client gemini;
        private readonly BooksService googleBooks;

        /// <summary>
        /// Initializes the book service.
        /// </summary>
        /// <param name="bookRepository">The book repository.</param>
        /// <param name="blobStorageService">The blob storage service.</param>
        /// <param name="gemini">The gemini client.</param>
        /// <param name="googleBooks">The google books service.</param>
        /// <param name="cachedBookEmbeddingsProvider">The cached book embeddings provider.</param>
        public BookService(IBookRepository bookRepository, IBlobStorageService blobStorageService, ICachedBookDetailsProvider cachedBookEmbeddingsProvider, Client gemini, BooksService googleBooks)
        {
            this.bookRepository = bookRepository;
            this.blobStorageService = blobStorageService;
            this.gemini = gemini;
            this.googleBooks = googleBooks;
            this.cachedBookEmbeddingsProvider = cachedBookEmbeddingsProvider;
        }

        /// <summary>
        /// Adds the books.
        /// </summary>
        /// <param name="books">The books to add.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the add operation.</returns>
        public async Task AddBooksAsync(IEnumerable<BookDetails> books, CancellationToken cancellationToken = default)
        {
            if (books == null || !books.Any())
            {
                return;
            }

            var numBooks = books.Count();
            var booksToAdd = new List<Book>();
            foreach (var batch in books.Chunk(batchSize))
            {
                var isbns = batch.Select(book => book.Isbn!).ToHashSet();
                var existingBooks = await this.bookRepository.GetByIsbnsAsync(isbns, false, cancellationToken);
                var existingIsbns = existingBooks.Select(book => book.Isbn).ToHashSet();
                var newBooks = batch.Where(book => !existingIsbns.Contains(book.Isbn!)).ToList();
                var uncachedBooks = new List<BookDetails>();

                if (newBooks.Count > 0)
                {
                    // Fetch embeddings from cache since earlier shelf lookups maybe have generated embeddings for these new books already.
                    foreach (var book in newBooks)
                    {
                        var cachedBook = await this.cachedBookEmbeddingsProvider.GetBookDetailsAsync(book, cancellationToken);
                        if (cachedBook.HasValue)
                        {
                            var bookToAdd = new Book(book.Isbn!, cachedBook.Value.Book.Title!, cachedBook.Value.Book.Author!, cachedBook.Value.Book.BookType, cachedBook.Value.Book.GenreGroup, cachedBook.Value.Embedding);
                            booksToAdd.Add(bookToAdd);
                        }
                        else
                        {
                            uncachedBooks.Add(book);
                        }
                    }

                    // Categorize and compute embeddings for uncached books.
                    var categorisedBooks = await CategorizeBooks(uncachedBooks, cancellationToken);
                    var fetchedBookPairs = await FetchBooks(categorisedBooks, cancellationToken);

                    // Log unidentified books here.
                    // Continue to add remaining books to the database.

                    var identifiableBooks = fetchedBookPairs.Where(b => b.Value != null).Select(b => (b.Key, b.Value)).ToList();
                    var bookEmbeddings = await this.CalculateEmbeddingsAsync(identifiableBooks, cancellationToken);

                    foreach (var (book, embedding) in bookEmbeddings)
                    {
                        // No need to add book if there is no embedding because without an embedding the book is not useful for recommendations.
                        if (embedding.HasValue)
                        {
                            var bookToAdd = new Book(book.Isbn!, book.Title!, book.Author!, book.BookType, book.GenreGroup, embedding.Value);
                            booksToAdd.Add(bookToAdd);
                            await this.cachedBookEmbeddingsProvider.SetBookDetailsAsync(book.Title, book.Author, book, embedding.Value, cancellationToken);
                        }
                    }

                    await Task.Delay(3000, cancellationToken);
                }
            }

            this.bookRepository.AddMultiple(booksToAdd);
            await this.bookRepository.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Deletes the book.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the delete operation.</returns>
        public async Task DeleteBookAsync(string isbn, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(isbn))
            {
                return;
            }

            var books = await this.bookRepository.GetByIsbnsAsync([isbn], true, cancellationToken);
            if (!books.Any())
            {
                return;
            }

            this.bookRepository.Delete(books.First());
            await this.bookRepository.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Gets book recommendations.
        /// </summary>
        /// <param name="imageUrl">The image url.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The recommended books.</returns>
        public async Task<RecommendationResultMessageDto> GetRecommendationsAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            // Dopwnload the image from blob storage.
            var image = await this.blobStorageService.DownloadImageAsync(imageUrl, cancellationToken);

            // Extract book details from the image using OCR.
            var extractedBooks = await ExtractBooksFromImage(image, cancellationToken);
            extractedBooks = extractedBooks.OrderBy(b => b.Title).ThenBy(b => b.Author).ToList();
            var bookEmbeddings = new List<(CategorisedBook Book, SqlVector<float> Vector)>();
            var allExtractedBooks = new List<CategorisedBook>();
            foreach (var batch in extractedBooks.Chunk(batchSize))
            {
                // Fetch books embeddings that are in the cache.
                var uncachedBooks = new List<CategorisedBook>();
                var cachedBooks = new List<CategorisedBook>();
                foreach (var book in batch)
                {
                    var cachedBook = await this.cachedBookEmbeddingsProvider.GetBookDetailsAsync(book, cancellationToken);
                    if (cachedBook.HasValue)
                    {
                        bookEmbeddings.Add((book, cachedBook.Value.Embedding));
                        cachedBooks.Add(book);
                    }
                    else
                    {
                        uncachedBooks.Add(book);
                    }
                }

                // Fetch the book details from Google Books API.
                var booksData = await FetchBooks(uncachedBooks, cancellationToken);

                // Log unidentified books here.
                // Continue to suggest recommendations for the identified books.

                // Calculate embeddings for the books.
                var identifiableBooks = booksData.Where(b => b.Value != null).Select(b => (b.Key, b.Value)).ToList();
                var newBookEmbeddings = await this.CalculateEmbeddingsAsync(identifiableBooks, cancellationToken);
                var successfulEmbeddings = newBookEmbeddings.Where(b => b.Vector.HasValue).Select(b => (b.Book, b.Vector!.Value)).ToList();

                foreach (var bookEmbedding in successfulEmbeddings)
                {
                    // We need to store the cache with both the extracted (title, author) as well as the fetched (title, author) because
                    // they may not match exactly. For example, the extracted title may be "The Great Gatsby" while the fetched title from Google Books API may be "The Great Gatsby: A Novel".
                    // Goodle Books API can fix minor errors in the OCR extracted text, so we want to cache the corrected version as well.
                    if (booksData.TryGetValue(bookEmbedding.Book, out var bookDetails))
                    {
                        await this.cachedBookEmbeddingsProvider.SetBookDetailsAsync(bookDetails?.VolumeInfo?.Title, bookDetails?.VolumeInfo?.Authors?.FirstOrDefault(), bookEmbedding.Book, bookEmbedding.Value, cancellationToken);
                    }

                    await this.cachedBookEmbeddingsProvider.SetBookDetailsAsync(bookEmbedding.Book.Title, bookEmbedding.Book.Author, bookEmbedding.Book, bookEmbedding.Value, cancellationToken);

                    // Update the book details to match the fetched book details from Google Books API if available.
                    // This is so that minor OCR extraction errors can be fixed.
                    bookEmbedding.Book.Title = bookDetails?.VolumeInfo?.Title ?? bookEmbedding.Book.Title;
                    bookEmbedding.Book.Author = bookDetails?.VolumeInfo?.Authors?.FirstOrDefault() ?? bookEmbedding.Book.Author;
                }

                allExtractedBooks.AddRange(cachedBooks.Concat(identifiableBooks.Select(b => b.Key)).ToList());
                bookEmbeddings.AddRange(successfulEmbeddings);
                await Task.Delay(3000, cancellationToken);
            }

            
            var recommendedBooks = await this.bookRepository.GetRecommendedBooksAsync(bookEmbeddings, 7, cancellationToken);
            return new RecommendationResultMessageDto
            {
                ExtractedBooks = allExtractedBooks.Select(b => new ExtractedBookDto
                {
                    Name = b.Title!,
                    Author = b.Author
                }).ToList(),
                RecommendedBooks = recommendedBooks.Select(b => new RecommendedBookDto
                {
                    Isbn = b.Isbn,
                    Name = b.Name
                }).ToList(),
                Error = null
            };
        }

        private async Task<List<CategorisedBook>> ExtractBooksFromImage(ImageData image, CancellationToken cancellationToken)
        {
            var contents = new Content
            {
                Parts =
                [
                        new Part { Text = ocrExtractionPrompt },
                        new Part
                        {
                            InlineData = new Blob
                            {
                                MimeType = image.Type,
                                Data = image.Data
                            }
                        }
                ]
            };

            var config = new GenerateContentConfig
            {
                ResponseMimeType = "application/json",
                ResponseSchema = new Schema
                {
                    Type = Type.Array,
                    Items = new Schema
                    {
                        Type = Type.Object,
                        Properties = new Dictionary<string, Schema>
                        {
                            ["title"] = new Schema { Type = Type.String },
                            ["author"] = new Schema { Type = Type.String },
                            ["bookType"] = new Schema { Type = Type.String, Format = "enum", Enum = Enum.GetNames(typeof(BookType)).ToList() },
                            ["genreGroup"] = new Schema { Type = Type.String, Format = "enum", Enum = Enum.GetNames(typeof(GenreGroup)).ToList() },
                            ["bookTags"] = new Schema
                            {
                                Type = Type.Array,
                                Items = new Schema { Type = Type.String },
                                MaxItems = 3
                            }
                        }
                    }
                }
            };

            // Extract the book titles and authors from the image using OCR.
            // TODO: Add retry logic and rate limiting here.
            var result = await this.gemini.Models.GenerateContentAsync("gemini-3.5-flash", contents, config, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var extractedBooks = JsonSerializer.Deserialize<List<CategorisedBook>>(result.Text ?? "[]", options) ?? [];
            return extractedBooks;
        }

        private async Task<List<CategorisedBook>> CategorizeBooks(IEnumerable<BookDetails> books, CancellationToken cancellationToken)
        {
            if (!books.Any())
            {
                return [];
            }

            var contents = new Content
            {
                Parts =
                [
                    new Part { Text = bookClassificationPrompt },
                    new Part { Text =  JsonSerializer.Serialize(books) }
                ]
            };

            var config = new GenerateContentConfig
            {
                ResponseMimeType = "application/json",
                ResponseSchema = new Schema
                {
                    Type = Type.Array,
                    Items = new Schema
                    {
                        Type = Type.Object,
                        Properties = new Dictionary<string, Schema>
                        {
                            ["isbn"] = new Schema { Type = Type.String },
                            ["title"] = new Schema { Type = Type.String },
                            ["author"] = new Schema { Type = Type.String },
                            ["bookType"] = new Schema { Type = Type.String, Format = "enum", Enum = Enum.GetNames(typeof(BookType)).ToList() },
                            ["genreGroup"] = new Schema { Type = Type.String, Format = "enum", Enum = Enum.GetNames(typeof(GenreGroup)).ToList() },
                            ["bookTags"] = new Schema
                            {
                                Type = Type.Array,
                                Items = new Schema { Type = Type.String },
                                MaxItems = 3
                            }
                        }
                    }
                }
            };

            // TODO: Add retry logic and rate limiting here.
            var result = await this.gemini.Models.GenerateContentAsync("gemini-3.5-flash", contents, config, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var categorisedBooks = JsonSerializer.Deserialize<List<CategorisedBook>>(result.Text ?? "[]", options) ?? [];
            return categorisedBooks;
        }

        private async Task<ConcurrentDictionary<CategorisedBook, Volume?>> FetchBooks(List<CategorisedBook> bookDetails, CancellationToken cancellationToken)
        {
            // Fetches in parallel.
            // Running too many parallel requests may result in 429 errors because of rate limiting by the Google Books API.
            // So we need to limit the number of parallel requests to avoid hitting the rate limits.
            // ConcurrentDictionary is a thread safe collection that allows multiple threads to add items to it concurrently without throwing exceptions.
            var result = new ConcurrentDictionary<CategorisedBook, Volume?>();
            await Parallel.ForEachAsync(
                bookDetails,
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = cancellationToken
                },
                async (book, cancellationToken) =>
                {
                    var maxAttempts = 3;
                    for (var i = 1; i <= maxAttempts; i++)
                    {
                        try
                        {
                            if (book.Isbn == null && book.Title == null)
                            {
                                return;
                            }

                            var query = book.Isbn != null ? $"isbn:{book.Isbn}" : book.Author != null ? $"intitle:{book.Title} {book.Author}" : $"intitle:{book.Title}";
                            var bookRequest = this.googleBooks.Volumes.List(query);

                            var response = await bookRequest.ExecuteAsync(cancellationToken);

                            var volume = response?.Items?.FirstOrDefault();

                            if (volume == null && book.Title != null)
                            {
                                // Fallback to looser query
                                query = $"{book.Title}";
                                bookRequest = this.googleBooks.Volumes.List(query);
                                response = await bookRequest.ExecuteAsync(cancellationToken);

                                // Search by last name to avoid issues with middle names or initials in the author name.
                                var lastName = book.Author?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                                volume = response?.Items?.FirstOrDefault(x => string.IsNullOrWhiteSpace(lastName) || x.VolumeInfo?.Authors?.Any(author => author.Contains(lastName, StringComparison.OrdinalIgnoreCase)) == true);
                            }

                            result.TryAdd(book, volume);
                            return;
                        }
                        catch (GoogleApiException ex) when (i < maxAttempts && IsTransientFailure(ex.HttpStatusCode))
                        {
                            // Random.Shared() is thread safe.
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i) + Random.Shared.NextDouble() * 2), cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Log the error here and let it throw so that Azure can retry the request.
                            throw;
                        }
                    }
                }
            );

            // TODO: Check language in image and try to find book with same language from the booksData.
            // That way we can consider the language as parameter for suggesting recommendations too.
            return result;
        }

        private static bool IsTransientFailure(HttpStatusCode code) => code is HttpStatusCode.RequestTimeout 
                or HttpStatusCode.InternalServerError 
                or HttpStatusCode.BadGateway 
                or HttpStatusCode.ServiceUnavailable 
                or HttpStatusCode.GatewayTimeout 
                or HttpStatusCode.TooManyRequests 
                or HttpStatusCode.ServiceUnavailable;

        private async Task<List<(CategorisedBook Book, SqlVector<float>? Vector)>> CalculateEmbeddingsAsync(List<(CategorisedBook Book, Volume? Volume)> books, CancellationToken cancellationToken)
        {
            if (books.Count == 0)
            {
                return [];
            }

            // If we were running queries against the stored books in the database to allow users to find books based on a description of the kind of book they want,
            // we would use "RETRIEVAL_QUERY" as the task type for the input query embedding and "RETRIEVAL_DOCUMENT" as the task type for the stored book embeddings in the database.
            // This would do an asymmetrical search where the input query embedding is used to find the most similar stored book embeddings in the database.
            // In this case however, we are doing a symmetrical search to find books similar to the input books so for this we need to use
            // "SEMANTIC_SIMILARITY" as the task type for both the input book embeddings and the stored book embeddings in the database.
            // Smaller output dimensionality will result in faster searches and less storage space.
            // For storing book embeddings this is enough and shouldn't really affect the accuracy of the recommendation search results.
            var embeddingConfig = new EmbedContentConfig
            {
                TaskType = "SEMANTIC_SIMILARITY",
                OutputDimensionality = 768
            };

            // Here, the field weighting depends on the model.
            // If we want to give more weight to a certain field could use multiple embedding vectors
            // and do a similarity search for each vector.
            // Then multiply the similarity scores with the weights to get the final score.
            // Final score = weight1 * similarityScore1 + weight2 * similarityScore2 + ... + weightN * similarityScoreN
            // For now, we can just let the model decide and tune the weights if needed.
            var embeddingContent = new List<Content>();
            foreach (var book in books)
            {
                // Use sentence structure so that the model does soft semantic weighting instead of hard keyword matching.
                // This is because the model will understand the context of the fields better and will be able to give more weight to the important fields.
                var fieldCandidates = new[] { book.Volume!.VolumeInfo?.Title, book.Volume.VolumeInfo?.Authors?.FirstOrDefault(), book.Volume.VolumeInfo?.Description, $"The book captures the vibe of {string.Join(", ", book.Book.BookTags)}" };
                var fields = new StringBuilder();
                foreach (var candidate in fieldCandidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        fields.Append($"{candidate}. ");
                    }
                }

                embeddingContent.Add(new Content
                {
                    Parts =
                        [
                            new Part
                            {
                                Text = fields.ToString().TrimEnd()
                            }
                        ]
                });
            }

            // From Gemini docs: While the default 3072-dimension embeddings are always normalized, Gemini Embedding 2 also auto-normalizes truncated dimensions (e.g., 768, 1536).
            // This ensures semantic similarity is calculated via vector direction rather than magnitude, providing more accurate results out of the box.
            // Older Models: If you are using gemini-embedding - 001, you must manually normalize non - 3072 dimensions.
            // So we use "gemini-embedding-2" for embeddings since we need normalized embeddings to calculate cosine similarity for book recommendations.
            var embedding = await this.gemini.Models.EmbedContentAsync(
                "gemini-embedding-2",
                contents: embeddingContent!,
                embeddingConfig,
                cancellationToken);
            var sqlVectors = new List<SqlVector<float>?>();
            foreach (var vector in embedding.Embeddings ?? [])
            {
                if (vector.Values == null)
                {
                    sqlVectors.Add(null);
                }
                else
                {
                    var sqlVector = new SqlVector<float>(vector.Values.Select(value => (float)value).ToArray());
                    sqlVectors.Add(sqlVector);
                }
            }

            // Embeddings are ordered in the same order as the inout books so we can zip it.
            var bookEmbeddings = books.Zip(sqlVectors ?? [], (book, vector) => (book.Book, vector)).ToList();
            return bookEmbeddings;
        }
    }
}

// Design choice: Gemini 3.8 flash for ocr extraction since it can handle non standard text extraction which is required for book shelf images which has text on the 
// book spines in vertical orientation and fancy fonts.
// It is also able to cluster information to identify what information belongs to which book.
// For example, it can extract book names and author names and identify which author belongs to which book.
// It can also identify individual books rather than just returning a long list of text which would then need to be clustered and grouped into individual books.

// On free tier, google will use the datat to train its models which is okay for book shelf images.