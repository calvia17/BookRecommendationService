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
    [JsonDerivedType(typeof(RecommendationsRequestedEvent), "RecommendationsRequested")]
    public abstract class BookEvent
    {
    }
}
