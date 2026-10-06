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
            ArgumentNullException.ThrowIfNull(books);

            if (books.Any())
            {
                var isbns = new HashSet<string>();
                var duplicates = new HashSet<string>();
                foreach (var newBookData in books)
                {
                    if (!isbns.Add(newBookData.Isbn))
                    {
                        duplicates.Add(newBookData.Isbn);
                    }
                }
                if (duplicates.Count > 0)
                {
                    throw new DuplicateBookInputException(duplicates);
                }

                var duplicateBooks = await this.bookRepository.GetByIsbnsAsync(isbns, false, cancellationToken);
                if (duplicateBooks.Any())
                {
                    var conflicts = duplicateBooks.Select(db => new ExistingBook(db.Id, db.Isbn, db.Name)).ToList();
                    throw new BookAlreadyExistsException(conflicts);
                }

                var bookDetails = books.Select(b => new BookDetails(b.Isbn, b.Name, null)).ToList();
                var fetchedBookPairs = await FetchBooks(bookDetails, cancellationToken);
                if (fetchedBookPairs == null)
                {
                    // None of the books could be identified.
                    return;
                }

                var unidentifiableBooks = fetchedBookPairs.Where(b => b.Volume == null).Select(b => b.Book).ToList();
                // Log message here to indicate that some books could not be identified and will not be added to the database.
                // Continue to add remaining books to the database.

                // TODO: check if the embedding for a book in embeddings is null and that order of embeddings matches the order of booksData.
                // Otherwise we may need to calculate embeddings separately.
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
                throw new ArgumentException("Invalid ISBN provided.", nameof(isbn));
            }

            var books = await this.bookRepository.GetByIsbnsAsync([isbn], true, cancellationToken);
            if (!books.Any())
            {
                throw new BookNotFoundException(isbn);
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
            var contents = new Content
            {
                Parts =
                    [
                        new Part
                        {
                            Text = "Extract the title and author name for each book from the book spines in the book shelf image."
                        },
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
                            ["author"] = new Schema { Type = Type.String }
                        }
                    }
                }
            };

            // Extract the book titles and authors from the image using OCR.
            // TODO: Add retry logic and rate limiting here.
            var result = await this.gemini.Models.GenerateContentAsync("gemini-3.5-flash", contents, config, cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var extractedBooks = JsonSerializer.Deserialize<List<BookDetails>>(result.Text!, options) ?? [];

            if (extractedBooks.Count == 0)
            {
                throw new BookExtractionException();
            }

            // Fetch the book details from Google Books API.
            var booksData = await FetchBooks(extractedBooks, cancellationToken);
            if (booksData == null || booksData.Count == 0)
            {
                throw new BookIdentificationException();
            }

            var unidentifiableBooks = booksData.Where(b => b.Volume == null).Select(b => b.Book).ToList();
            // Log message here to indicate that some books could not be identified and will not be added to the database.

            // Calculate embeddings for the books.
            var identifiableBooks = booksData.Where(b => b.Volume != null).ToList();
            var bookEmbeddings = await this.CalculateEmbeddingsAsync(identifiableBooks, cancellationToken);
            var successfulEmbeddings = bookEmbeddings.Where(b => b.Vector.HasValue).Select(b => (b.Book, b.Vector!.Value)).ToList();
            if (successfulEmbeddings.Count == 0)
            {
                // Cannot suggest recommendations because embeddings could not be calculated for any of the extracted books.
                // If only some embeddings could not be calculated, we can still suggest recommendations for the books that have embeddings.
                throw new EmbeddingCalculationException();
            }

            var recommendedBooks = await this.bookRepository.GetRecommendedBooksAsync(successfulEmbeddings, 5, cancellationToken);

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

        private async Task<List<(BookDetails Book, Volume? Volume)>?> FetchBooks(List<BookDetails> bookDetails, CancellationToken cancellationToken)
        {
            var bookRequests = new List<(BookDetails Book, VolumesResource.ListRequest Request)>();
            foreach (var book in bookDetails)
            {
                if (book.Isbn != null)
                {
                    bookRequests.Add((book, this.googleBooks.Volumes.List($"isbn:{book.Isbn}")));
                }
                else if (book.Title != null)
                {
                    // The OCR extraction may not be perfect so we  need to be ensure that the title and author have been
                    // extracted before querying the Google Books API.
                    var query = book.Author != null ? $"intitle:{book.Title}+inauthor:{book.Author}" : $"intitle:{book.Title}";
                    bookRequests.Add((book, this.googleBooks.Volumes.List(query)));
                }

                // If the book has no isbn, title or author, ignore it.
            }

            if (bookRequests.Count == 0)
            {
                return null;
            }

            // Fetches in parallel.
            // Running too many parallel requests may result in 429 errors because of rate limiting by the Google Books API.
            // So we need to limit the number of parallel requests to avoid hitting the rate limits.
            // ConcurrentBag is a thread safe collection that allows multiple threads to add items to it concurrently without throwing exceptions.
            var result = new ConcurrentBag<(BookDetails Book, Volume? Volume)>();
            await Parallel.ForEachAsync(
                bookRequests,
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = cancellationToken
                },
                async (bookRequest, cancellationToken) =>
                {
                    try
                    {
                        var response = await bookRequest.Request.ExecuteAsync(cancellationToken);
                        var volume = response?.Items?.FirstOrDefault();

                        // If the book could not be found, volume will be null.
                        result.Add((bookRequest.Book, volume));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // TODO: Add retry logic and rate limiting here to handle transient errors such as network issues or rate limiting by the Google Books API.
                        throw new BookLookupFailedException(bookRequest.Book.Isbn, bookRequest.Book.Title, ex.Message);
                    }
                }
            );

            // TODO: Check language in image and try to find book with same language from the booksData.
            // That way we can consider the language as parameter for suggesting recommendations too.
            return result.ToList();
        }

        private async Task<List<(BookDetails Book, SqlVector<float>? Vector)>> CalculateEmbeddingsAsync(List<(BookDetails Book, Volume? Volume)> books, CancellationToken cancellationToken)
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
                embeddingContent.Add(new Content
                {
                    Parts =
                        [
                            new Part
                            {
                                Text = $"Title: {book.Volume!.VolumeInfo.Title}, Subtitle: {book.Volume.VolumeInfo.Subtitle}, Series: {book.Volume.VolumeInfo.SeriesInfo}, Author: {book.Volume.VolumeInfo.Authors.FirstOrDefault()}, Genres: {string.Join(", ", book.Volume.VolumeInfo.Categories)}, Summary: {book.Volume.VolumeInfo.Description}"
                            }
                        ]
                });
            }

            var embedding = await this.gemini.Models.EmbedContentAsync(
                "gemini-embedding-001",
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