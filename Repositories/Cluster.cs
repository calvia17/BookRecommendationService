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
        /// Initializes the cluster.
        /// </summary>
        /// <param name="centroid">The centroid of the cluster.</param>
        /// <param name="size">The size of the cluster.</param>
        public Cluster(float[] centroid, int size)
        {
            this.Centroid = centroid;
            this.Size = size;
        }
    }
}
