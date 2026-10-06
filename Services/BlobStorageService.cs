using Azure;
using Azure.Storage.Blobs;
using RabbitHole.Vision.Worker.Exceptions;
using SixLabors.ImageSharp;

namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The blob storage service.
    /// </summary>
    public class BlobStorageService : IBlobStorageService
    {
        private const int MaxImageSize = 10 * 1024 * 1024; // 10 MB
        private static readonly string[] AllowedImageFormats = { ".jpeg", ".jpg", ".png" };
        private readonly string blobStorageHost;
        private readonly BlobContainerClient blobContainer;

        public BlobStorageService(BlobContainerClient blobContainer, string blobStorageHost)
        {
            this.blobContainer = blobContainer;
            this.blobStorageHost = blobStorageHost;
        }

        /// <summary>
        /// Downloads an image from blob storage.
        /// </summary>
        /// <param name="imageUrl">The url of the image to download.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The downloaded image.</returns>
        public async Task<ImageData> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(imageUrl);
            
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"Invalid image URL: {imageUrl}", nameof(imageUrl));
            }

            if (!uri.Host.Equals(this.blobStorageHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Image URL host is not a trusted storage host.", nameof(imageUrl));
            }

            var blobUriBuilder = new BlobUriBuilder(uri);
            var blobClient = this.blobContainer.GetBlobClient(blobUriBuilder.BlobName);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (properties.Value.ContentLength == 0)
            {
                throw new InvalidImageException("Image is empty.");
            }

            if (properties.Value.ContentLength > MaxImageSize)
            {
                throw new InvalidImageException($"Image size exceeds the maximum limit of {MaxImageSize / (1024 * 1024)} MB");
            }

            try
            {
                var image = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                using var imageValue = image.Value;
                var imageStream = new MemoryStream();
                await imageValue.Content.CopyToAsync(imageStream, cancellationToken);
                imageStream.Position = 0;
                var imageInfo = await Image.IdentifyAsync(imageStream, cancellationToken);
                var extension = imageInfo.Metadata.DecodedImageFormat?.Name.ToLower();
                if (imageInfo == null
                    || imageInfo.Width == 0
                    || imageInfo.Height == 0
                    || imageInfo.Metadata == null
                    || imageInfo.Metadata.DecodedImageFormat == null
                    || !AllowedImageFormats.Contains($".{extension}"))
                {
                    throw new InvalidImageException("Invalid image file");
                }

                imageStream.Position = 0;
                var type = extension == "png" ? "image/png" : "image/jpeg";
                return new ImageData(imageStream.ToArray(), type);
            }
            catch (RequestFailedException)
            {
                throw new InvalidImageException("Failed to download image from blob storage.");
            }
            catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException)
            {
                throw new InvalidImageException("Invalid image file");
            }
        }
    }
}
