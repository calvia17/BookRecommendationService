using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker.Objects;

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
        /// Saves the changes.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task representing the save operation.</returns>
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await this.context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Deletes the book.
        /// </summary>
        /// <param name="isbn">The isbn.</param>
        public async Task DeleteAsync(string isbn)
        {
            var book = await this.context.Books
                .FirstOrDefaultAsync(b => b.Isbn == isbn);
            if (book != null)
            {
                this.context.Books.Remove(book);
            }
        }
    }
}
