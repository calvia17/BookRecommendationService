using System;
using System.Collections.Generic;
using System.Text;

namespace RabbitHole.Vision.Worker.Exceptions
{
    /// <summary>
    /// The invalid image exception.
    /// </summary>
    public class InvalidImageException : BaseApplicationException
    {
        /// <summary>
        /// Initializes the invalid image exception.
        /// </summary>
        /// <param name="message">The message.</param>
        public InvalidImageException(string message) : base(message)
        {
        }
    }
}
