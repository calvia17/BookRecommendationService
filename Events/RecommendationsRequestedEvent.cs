namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The recommendations requested event.
    /// </summary>
    public class RecommendationsRequestedEvent : BookEvent
    {
        /// <summary>
        /// The book shelf image.
        /// </summary>
        public byte[] ShelfImage { get; }

        /// <summary>
        /// Initializes the recommendations requested event.
        /// </summary>
        /// <param name="shelfImage">The book shelf image.</param>
        public RecommendationsRequestedEvent(byte[] shelfImage)
            : base(BookEventType.RecommendationsRequested)
        {
            this.ShelfImage = shelfImage;
        }
    }
}
