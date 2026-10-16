using RabbitHole.Vision.Worker.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace RabbitHole.Vision.Worker.Repositories
{
    /// <summary>
    /// The cluster class.
    /// </summary>
    public class Cluster
    {
        /// <summary>
        /// Gets or sets the centroid.
        /// </summary>
        public float[] Centroid { get; set; }

        /// <summary>
        /// Gets or sets the size.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// Gets the books.
        /// </summary>
        public List<CategorisedBook> Books { get; }

        /// <summary>
        /// Initializes the cluster.
        /// </summary>
        /// <param name="centroid">The centroid of the cluster.</param>
        /// <param name="initialBook">The initial book in the cluster.</param>
        public Cluster(float[] centroid, CategorisedBook initialBook)
        {
            this.Centroid = centroid;
            this.Size = 1;
            this.Books = [initialBook];
        }
    }
}
