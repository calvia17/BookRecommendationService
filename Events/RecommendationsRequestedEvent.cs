namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The recommendations requested event.
    /// </summary>
    public class RecommendationsRequestedEvent : BookEvent
    {
        /// <summary>
        /// The book shelf image blob url.
        /// </summary>
        public string ImageBlobUrl { get; }

        /// <summary>
        /// Initializes the recommendations requested event.
        /// </summary>
        /// <param name="imageBlobUrl">The book shelf image blob url.</param>
        public RecommendationsRequestedEvent(string imageBlobUrl)
        {
            this.ImageBlobUrl = imageBlobUrl;
        }
    }
}
