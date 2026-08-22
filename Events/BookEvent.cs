using System.Text.Json.Serialization;

namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The book event.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
    [JsonDerivedType(typeof(BooksAddedEvent), "Added")]
    [JsonDerivedType(typeof(BooksUpdatedEvent), "Updated")]
    [JsonDerivedType(typeof(BookDeletedEvent), "Deleted")]
    public class BookEvent
    {
        /// <summary>
        /// The book event type.
        /// </summary>
        public BookEventType EventType { get; }

        /// <summary>
        /// Initializes the book event.
        /// </summary>
        /// <param name="eventType">The book event type.</param>
        public BookEvent(BookEventType eventType)
        {
            this.EventType = eventType;
        }
    }
}
