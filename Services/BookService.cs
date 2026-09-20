using Microsoft.Data.SqlTypes;
using RabbitHole.Vision.Worker.Dtos;
using RabbitHole.Vision.Worker.Exceptions;
using RabbitHole.Vision.Worker.Objects;
using RabbitHole.Vision.Worker.Repositories;
using static RabbitHole.Vision.Worker.Events.BooksAddedEvent;
using static RabbitHole.Vision.Worker.Events.BooksUpdatedEvent;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The book service.
    /// </summary>
    public class BookService : IBookService
    {
        private readonly IBookRepository bookRepository;

        /// <summary>
        /// Initializes the book service.
        /// </summary>
        /// <param name="bookRepository">The book repository.</param>
        public BookService(IBookRepository bookRepository)
        {
            this.bookRepository = bookRepository;
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

                var booksToAdd = new List<Book>();
                foreach (var book in books)
                {
                    var embedding = CalculateEmbedding(book.Isbn, book.Name, book.Genres);
                    var bookToAdd = new Book(book.Isbn, book.Name, embedding);
                    booksToAdd.Add(bookToAdd);
                }

                this.bookRepository.AddMultiple(booksToAdd);
                await this.bookRepository.SaveChangesAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Updates the books.
        /// </summary>
        /// <param name="books">The books to update.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the update operation.</returns>
        public async Task UpdateBooksAsync(IEnumerable<BooksUpdatedData> books, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(books);

            if (books.Any(x => string.IsNullOrEmpty(x.Isbn)))
            {
                throw new ArgumentException("One or more books have an invalid ISBN.", nameof(books));
            }

            var bookIsbns = new HashSet<string>();
            var duplicates = new HashSet<string>();
            foreach (var bookData in books)
            {
                if (!bookIsbns.Add(bookData.Isbn))
                {
                    duplicates.Add(bookData.Isbn);
                }
            }
            if (duplicates.Count > 0)
            {
                throw new DuplicateBookInputException(duplicates);
            }

            var booksToUpdate = await this.bookRepository.GetByIsbnsAsync(bookIsbns, true, cancellationToken);
            var notFoundBookIsbns = bookIsbns.Except(booksToUpdate.Select(b => b.Isbn)).ToList();
            if (notFoundBookIsbns.Count > 0)
            {
                throw new BookNotFoundException(notFoundBookIsbns);
            }

            var booksMap = books.ToDictionary(b => b.Isbn, b => b);
            foreach (var book in booksToUpdate)
            {
                var updatedBook = booksMap[book.Isbn];
                book.Name = updatedBook.Name;
                book.Embedding = CalculateEmbedding(book.Isbn, book.Name, updatedBook.Genres);
            }

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
            // TODO: Implement this.
            return new RecommendationResultMessageDto
            {
                ExtractedBooks = [],
                RecommendedBooks = [],
                Error = null
            };
        }

        private static SqlVector<float> CalculateEmbedding(string isbn, string name, IEnumerable<GenreType> genres)
        {
            // TODO: Implement this.
            return new SqlVector<float>(new float[] { 0.1f, 0.2f, 0.3f });
        }
    }
}
