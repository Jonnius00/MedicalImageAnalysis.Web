using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MedicalImageAnalysis.Web.Services
{
    /// <summary>
    /// Service for performing K-means clustering on image data.
    /// </summary>
    public class KMeansService
    {
        private readonly Random _random;

        public KMeansService()
        {
            // Use a fixed seed for reproducibility
            _random = new Random(42);
        }

        /// <summary>
        /// Applies K-means clustering to grayscale pixel data.
        /// </summary>
        /// <param name="grayscalePixels">The grayscale pixel values (0-255)</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="k">Number of clusters</param>
        /// <param name="maxIterations">Maximum number of iterations</param>
        /// <returns>Cluster labels for each pixel</returns>
        public int[] ApplyKMeans(byte[] grayscalePixels, int width, int height, int k, int maxIterations = 100)
        {
            int pixelCount = width * height;
            
            // Convert byte array to double array for Math.NET Numerics
            var data = grayscalePixels.Select(p => (double)p).ToArray();
            
            // Initialize centroids randomly
            var centroids = InitializeCentroids(data, k);
            
            // Store cluster assignments for each pixel
            var labels = new int[pixelCount];
            
            // Iteratively update centroids and assignments
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Assign each pixel to the nearest centroid
                bool changed = AssignPixelsToClusters(data, centroids, labels);
                
                // Update centroids based on current assignments
                UpdateCentroids(data, labels, centroids);
                
                // If no assignments changed, we've converged
                if (!changed)
                    break;
            }
            
            return labels;
        }
        
        /// <summary>
        /// Initializes centroids by randomly selecting k pixel values.
        /// </summary>
        private double[] InitializeCentroids(double[] data, int k)
        {
            var centroids = new double[k];
            var distinctValues = data.Distinct().ToArray();
            
            // If we have fewer distinct values than k, adjust k
            int actualK = Math.Min(k, distinctValues.Length);
            
            // Randomly select k distinct values as initial centroids
            var selectedIndices = new HashSet<int>();
            for (int i = 0; i < actualK; i++)
            {
                int index;
                do
                {
                    index = _random.Next(distinctValues.Length);
                } while (selectedIndices.Contains(index));
                
                selectedIndices.Add(index);
                centroids[i] = distinctValues[index];
            }
            
            // Fill remaining centroids if necessary
            for (int i = actualK; i < k; i++)
            {
                centroids[i] = distinctValues[_random.Next(distinctValues.Length)];
            }
            
            return centroids;
        }
        
        /// <summary>
        /// Assigns each pixel to the nearest centroid.
        /// </summary>
        /// <returns> True if any assignments changed, false otherwise</returns>
        private bool AssignPixelsToClusters(double[] data, double[] centroids, int[] labels)
        {
            bool changed = false;
            
            for (int i = 0; i < data.Length; i++)
            {
                int nearestCentroid = FindNearestCentroid(data[i], centroids);
                
                if (labels[i] != nearestCentroid)
                {
                    labels[i] = nearestCentroid;
                    changed = true;
                }
            }
            
            return changed;
        }
        
        /// <summary>
        /// Finds the index of the nearest centroid to a pixel value.
        /// </summary>
        private int FindNearestCentroid(double pixelValue, double[] centroids)
        {
            int nearest = 0;
            double minDistance = Math.Abs(pixelValue - centroids[0]);
            
            for (int i = 1; i < centroids.Length; i++)
            {
                double distance = Math.Abs(pixelValue - centroids[i]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = i;
                }
            }
            
            return nearest;
        }
        
        /// <summary>
        /// Updates centroids based on current cluster assignments.
        /// </summary>
        private void UpdateCentroids(double[] data, int[] labels, double[] centroids)
        {
            var sums = new double[centroids.Length];
            var counts = new int[centroids.Length];
            
            // Sum values and count pixels for each cluster
            for (int i = 0; i < data.Length; i++)
            {
                sums[labels[i]] += data[i];
                counts[labels[i]]++;
            }
            
            // Calculate new centroids as mean of assigned pixels
            for (int i = 0; i < centroids.Length; i++)
            {
                if (counts[i] > 0)
                {
                    centroids[i] = sums[i] / counts[i];
                }
            }
        }
    }
}