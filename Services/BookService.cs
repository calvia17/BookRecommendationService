using Google;
using Google.Apis.Books.v1;
using Google.Apis.Books.v1.Data;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Data.SqlTypes;
using RabbitHole.Vision.Worker.Dtos;
using RabbitHole.Vision.Worker.Exceptions;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Repositories;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using static RabbitHole.Vision.Worker.Events.BooksAddedEvent;
using Type = Google.GenAI.Types.Type;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The book service.
    /// </summary>
    public class BookService : IBookService
    {
        private const string ocrExtractionPrompt = """
            Extract the following details for each book from the book spines in the book shelf image.
            - title: the title of the book (extract from the book spine)
            - author: the author of the book (extract from the book spine)
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
            - tags: up to 3 short lowercase tags (1-3 words each) describing the
              book's genre, subgenre, themes and main tropes, most defining first.
              Use common reader-recognized terms, for example "romantasy",
              "enemies to lovers", "vampires", "domestic thriller", "grief".
              Do not repeat the same idea in different words.
            Return one entry for every input book.
            """;

        private readonly IBookRepository bookRepository;
        private readonly IBlobStorageService blobStorageService;
        private readonly Client gemini;
        private readonly BooksService googleBooks;

        /// <summary>
        /// Initializes the book service.
        /// </summary>
        /// <param name="bookRepository">The book repository.</param>
        /// <param name="blobStorageService">The blob storage service.</param>
        /// <param name="gemini">The gemini client.</param>
        /// <param name="googleBooks">The google books service.</param>
        public BookService(IBookRepository bookRepository, IBlobStorageService blobStorageService, Client gemini, BooksService googleBooks)
        {
            this.bookRepository = bookRepository;
            this.blobStorageService = blobStorageService;
            this.gemini = gemini;
            this.googleBooks = googleBooks;
        }

        /// <summary>
        /// Adds the books.
        /// </summary>
        /// <param name="books">The books to add.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the add operation.</returns>
        public async Task AddBooksAsync(IEnumerable<BooksAddedData> books, CancellationToken cancellationToken = default)
        {
            if (books == null || !books.Any())
            {
                return;
            }

            var isbns = books.Select(book => book.Isbn).ToHashSet();
            var duplicateBooks = await this.bookRepository.GetByIsbnsAsync(isbns, false, cancellationToken);
            var duplicateIsbns = duplicateBooks.Select(book => book.Isbn).ToHashSet();
            var distinctBooks = books.Where(book => !duplicateIsbns.Contains(book.Isbn)).ToList();

            var bookDetails = distinctBooks.Select(b => new UncategorisedBook(b.Isbn, b.Name, b.Author)).ToList();
            var categorisedBooks = await CategorizeBooks(bookDetails, cancellationToken);

            var fetchedBookPairs = await FetchBooks(categorisedBooks, cancellationToken);

            // Log unidentified books here.
            // Continue to add remaining books to the database.

            var identifiableBooks = fetchedBookPairs.Where(b => b.Volume != null).ToList();
            var bookEmbeddings = await this.CalculateEmbeddingsAsync(identifiableBooks, cancellationToken);

            var booksToAdd = new List<Book>();
            foreach (var (book, embedding) in bookEmbeddings)
            {
                // No need to add book if there is no embedding because without an embedding the book is not useful for recommendations.
                if (embedding.HasValue)
                {
                    var bookToAdd = new Book(book.Isbn!, book.Title!, embedding.Value);
                    booksToAdd.Add(bookToAdd);
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

            // Fetch the book details from Google Books API.
            var booksData = await FetchBooks(extractedBooks, cancellationToken);

            // Log unidentified books here.
            // Continue to suggest recommendations for the identified books.

            // Calculate embeddings for the books.
            var identifiableBooks = booksData.Where(b => b.Volume != null).ToList();
            var bookEmbeddings = await this.CalculateEmbeddingsAsync(identifiableBooks, cancellationToken);
            var successfulEmbeddings = bookEmbeddings.Where(b => b.Vector.HasValue).Select(b => (b.Book, b.Vector!.Value)).ToList();

            var recommendedBooks = await this.bookRepository.GetRecommendedBooksAsync(successfulEmbeddings, 5, cancellationToken);

            // Fetch ISBN, name, author, genre and summary for each extracted book using Google Books API.
            // While gemini OCR is mostly reliable, small errors could occur.
            // Google Books API has some tolerance for errors in input so small errors can still return the right expected book.
            // So we re-fetch name and author as well so that the errors are corrected.

            // Calculate embeddings for each extracted book. We could also fetch visual data from the book covers using computer vision and add this information
            // to the embeddings. But this is not really necessary for a book recommendation system since the summary and genre are what really matter
            // for book recommendation systems. If it was an art recommendation system, then visual data would be more important.

            // Query the database for recommended books based on the embeddings. This can be done using nearest neighbour search algorithms such as
            // Euclidean distance, manhattan distance, cosine similarity, etc.
            // We can also use k nearest neighbour search algorithms to find the top k recommended books based on the embeddings.

            // If any of the input books are in the recommendatrion results, remove them from the list.

            // Consider putting a limit on the number of recommended books returned in the response to avoid overwhelming the user with too many recommendations.

            // Check if caching is required for stored books so that we don't have to query the database for every recommendation request.

            // Return the extracted books and recommended books in the response.

            // Add retries and rate limits for external API calls to avoid hitting rate limits and to handle transient errors.

            return new RecommendationResultMessageDto
            {
                ExtractedBooks = identifiableBooks.Select(b => new BookMessageDto
                {
                    Isbn = b.Book.Isbn!,
                    Name = b.Book.Title!
                }).ToList(),
                RecommendedBooks = recommendedBooks.Select(b => new BookMessageDto
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
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var extractedBooks = JsonSerializer.Deserialize<List<CategorisedBook>>(result.Text ?? "[]", options) ?? [];
            return extractedBooks;
        }

        private async Task<List<CategorisedBook>> CategorizeBooks(IEnumerable<UncategorisedBook> books, CancellationToken cancellationToken)
        {
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
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var categorisedBooks = JsonSerializer.Deserialize<List<CategorisedBook>>(result.Text ?? "[]", options) ?? [];
            return categorisedBooks;
        }

        private async Task<List<(CategorisedBook Book, Volume? Volume)>> FetchBooks(List<CategorisedBook> bookDetails, CancellationToken cancellationToken)
        {
            // Fetches in parallel.
            // Running too many parallel requests may result in 429 errors because of rate limiting by the Google Books API.
            // So we need to limit the number of parallel requests to avoid hitting the rate limits.
            // ConcurrentBag is a thread safe collection that allows multiple threads to add items to it concurrently without throwing exceptions.
            var result = new ConcurrentBag<(CategorisedBook Book, Volume? Volume)>();
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

                            // If the book could not be found, volume will be null.
                            result.Add((book, volume));
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
            return result.ToList();
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