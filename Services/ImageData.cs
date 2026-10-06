namespace RabbitHole.Vision.Worker.Services
{
    /// <summary>
    /// The image data.
    /// </summary>
    public class ImageData
    {
        /// <summary>
        /// The image data.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// The file type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Initializes the image data.
        /// </summary>
        /// <param name="data">The image data.</param>
        /// <param name="type">The file type.</param>
        public ImageData(byte[] data, string type)
        {
            this.Data = data; 
            this.Type = type;
        }

    }
}
