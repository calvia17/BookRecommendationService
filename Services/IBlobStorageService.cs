namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The blob storage service interface.
    /// </summary>
    public interface IBlobStorageService
    {
        /// <summary>
        /// Downloads an image from blob storage.
        /// </summary>
        /// <param name="imageUrl">The url of the image to download.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The downloaded image.</returns>
        Task<ImageData> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken = default);
    }
}
