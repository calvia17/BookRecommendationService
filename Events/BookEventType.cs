namespace RabbitHole.Vision.Worker.Events
{
    /// <summary>
    /// The book event type.
    /// </summary>
    public enum BookEventType
    {
        /// <summary>
        /// The book added event type.
        /// </summary>
        Added = 0,

        /// <summary>
        /// The book updated event type.
        /// </summary>
        Updated = 1,

        /// <summary>
        /// The book deleted event type.
        /// </summary>
        Deleted = 2,
        
        /// <summary>
        /// The recommendations requested event type.
        /// </summary>
        RecommendationsRequested = 3
    }
}
