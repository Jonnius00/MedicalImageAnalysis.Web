using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedicalImageAnalysis.Web.Services
{
    /// <summary>
    /// Service for performing watershed segmentation on image data.
    /// </summary>
    public class WatershedService
    {
        /// <summary>
        /// Applies watershed segmentation to grayscale pixel data.
        /// </summary>
        /// <param name="grayscalePixels">The grayscale pixel values (0-255)</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="otsuBinaryMask">Optional binary mask from Otsu thresholding</param>
        /// <returns>Labels for each pixel representing watershed regions</returns>
        public int[] ApplyWatershed(byte[] grayscalePixels, int width, int height, byte[]? otsuBinaryMask = null)
        {
            // If no binary mask provided, generate one using a simple threshold
            if (otsuBinaryMask == null)
            {
                otsuBinaryMask = GenerateBinaryMask(grayscalePixels, width, height);
            }

            // Compute distance transform
            var distanceMap = ComputeDistanceTransform(otsuBinaryMask, width, height);

            // Find markers (local maxima in distance map)
            var markers = FindMarkers(distanceMap, width, height);

            // Apply watershed algorithm
            var labels = PerformWatershed(distanceMap, markers, width, height);

            return labels;
        }

        /// <summary>
        /// Generates a binary mask from grayscale pixels using a simple thresholding approach.
        /// </summary>
        private byte[] GenerateBinaryMask(byte[] grayscalePixels, int width, int height)
        {
            var binaryMask = new byte[grayscalePixels.Length];
            // Simple threshold at midpoint
            byte threshold = 127;

            for (int i = 0; i < grayscalePixels.Length; i++)
            {
                binaryMask[i] = grayscalePixels[i] > threshold ? (byte)255 : (byte)0;
            }

            return binaryMask;
        }

        /// <summary>
        /// Computes a distance transform approximation for the binary image.
        /// </summary>
        private float[] ComputeDistanceTransform(byte[] binaryMask, int width, int height)
        {
            var distanceMap = new float[width * height];

            // Initialize distance map
            // Foreground pixels (255) get 0 distance, background pixels get large value
            for (int i = 0; i < binaryMask.Length; i++)
            {
                distanceMap[i] = binaryMask[i] == 255 ? 0 : float.MaxValue;
            }

            // Forward pass - top to bottom, left to right
            for (int y = 1; y < height; y++)
            {
                for (int x = 1; x < width; x++)
                {
                    int index = y * width + x;
                    if (distanceMap[index] != 0)
                    {
                        // Check neighbors (top, left, top-left, top-right)
                        float minDistance = distanceMap[index];
                        
                        // Top
                        minDistance = Math.Min(minDistance, distanceMap[(y - 1) * width + x] + 1);
                        
                        // Left
                        minDistance = Math.Min(minDistance, distanceMap[y * width + (x - 1)] + 1);
                        
                        // Top-left
                        minDistance = Math.Min(minDistance, distanceMap[(y - 1) * width + (x - 1)] + 1.414f);
                        
                        // Top-right
                        if (x < width - 1)
                        {
                            minDistance = Math.Min(minDistance, distanceMap[(y - 1) * width + (x + 1)] + 1.414f);
                        }
                        
                        distanceMap[index] = minDistance;
                    }
                }
            }

            // Backward pass - bottom to top, right to left
            for (int y = height - 2; y >= 0; y--)
            {
                for (int x = width - 2; x >= 0; x--)
                {
                    int index = y * width + x;
                    if (distanceMap[index] != 0)
                    {
                        // Check neighbors (bottom, right, bottom-left, bottom-right)
                        float minDistance = distanceMap[index];
                        
                        // Bottom
                        minDistance = Math.Min(minDistance, distanceMap[(y + 1) * width + x] + 1);
                        
                        // Right
                        minDistance = Math.Min(minDistance, distanceMap[y * width + (x + 1)] + 1);
                        
                        // Bottom-right
                        minDistance = Math.Min(minDistance, distanceMap[(y + 1) * width + (x + 1)] + 1.414f);
                        
                        // Bottom-left
                        if (x > 0)
                        {
                            minDistance = Math.Min(minDistance, distanceMap[(y + 1) * width + (x - 1)] + 1.414f);
                        }
                        
                        distanceMap[index] = minDistance;
                    }
                }
            }

            return distanceMap;
        }

        /// <summary>
        /// Finds local maxima in the distance map to use as markers.
        /// </summary>
        private List<Point> FindMarkers(float[] distanceMap, int width, int height)
        {
            var markers = new List<Point>();

            // Simple approach: find local maxima with a minimum distance value
            float minDistanceThreshold = 5.0f; // Minimum distance value to consider

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * width + x;
                    float centerValue = distanceMap[index];

                    // Skip if value is too low
                    if (centerValue < minDistanceThreshold)
                        continue;

                    // Check if this is a local maximum
                    bool isMaximum = true;
                    for (int dy = -1; dy <= 1 && isMaximum; dy++)
                    {
                        for (int dx = -1; dx <= 1 && isMaximum; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int neighborIndex = (y + dy) * width + (x + dx);
                            if (distanceMap[neighborIndex] >= centerValue)
                            {
                                isMaximum = false;
                            }
                        }
                    }

                    if (isMaximum)
                    {
                        markers.Add(new Point(x, y));
                    }
                }
            }

            return markers;
        }

        /// <summary>
        /// Performs the actual watershed transformation.
        /// </summary>
        private int[] PerformWatershed(float[] distanceMap, List<Point> markers, int width, int height)
        {
            var labels = new int[width * height];
            var queue = new Queue<(Point point, int label)>();

            // Initialize all labels to -1 (unprocessed)
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = -1;
            }

            // Label each marker with a unique ID and add to queue
            for (int i = 0; i < markers.Count; i++)
            {
                int index = markers[i].Y * width + markers[i].X;
                labels[index] = i + 1; // Label IDs start from 1
                queue.Enqueue((markers[i], i + 1));
            }

            // Process queue
            int[] dx = { -1, -1, -1, 0, 0, 1, 1, 1 };
            int[] dy = { -1, 0, 1, -1, 1, -1, 0, 1 };

            while (queue.Count > 0)
            {
                var (currentPoint, currentLabel) = queue.Dequeue();

                // Process all 8 neighbors
                for (int i = 0; i < 8; i++)
                {
                    int newX = currentPoint.X + dx[i];
                    int newY = currentPoint.Y + dy[i];

                    // Check bounds
                    if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                    {
                        int newIndex = newY * width + newX;

                        // If not yet labeled
                        if (labels[newIndex] == -1)
                        {
                            labels[newIndex] = currentLabel;
                            queue.Enqueue((new Point(newX, newY), currentLabel));
                        }
                    }
                }
            }

            // Set any remaining unlabeled pixels to 0 (background)
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == -1)
                {
                    labels[i] = 0;
                }
            }

            return labels;
        }
    }
}