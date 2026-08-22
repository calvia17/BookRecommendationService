using Microsoft.EntityFrameworkCore;
using RabbitHole.Vision.Worker.Objects;

namespace RabbitHole.Vision.Worker
{
    /// <summary>
    /// The book store database context.
    /// </summary>
    public partial class BookStoreContext : DbContext
    {
        /// <summary>
        /// Initializes the book store context.
        /// </summary>
        /// <param name="options">The options for the context.</param>
        public BookStoreContext(DbContextOptions<BookStoreContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Gets or sets the books.
        /// </summary>
        public DbSet<Book> Books { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Book>().HasKey(b => b.Id);
            modelBuilder.Entity<Book>().Property(b => b.Isbn).HasMaxLength(13).IsRequired();
            modelBuilder.Entity<Book>().HasIndex(b => b.Isbn).IsUnique().HasDatabaseName("Books_Isbn");
            modelBuilder.Entity<Book>().Property(b => b.Name).HasMaxLength(250).IsRequired();
            modelBuilder.Entity<Book>().Property(b => b.Embedding).IsRequired();
        }
    }
}
