using System;
using System.Collections.Generic;
using System.Drawing;

namespace MedicalImageAnalysis.Web.Services
{
    /// <summary>
    /// Service for performing region growing segmentation on image data.
    /// </summary>
    public class RegionGrowingService
    {
        /// <summary>
        /// Applies region growing segmentation to grayscale pixel data.
        /// </summary>
        /// <param name="grayscalePixels">The grayscale pixel values (0-255)</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="seedPoint">The seed point to start region growing from</param>
        /// <param name="tolerance">Intensity difference threshold for region inclusion</param>
        /// <returns>Binary mask where 1 indicates pixels belonging to the region, 0 otherwise</returns>
        public byte[] ApplyRegionGrowing(byte[] grayscalePixels, int width, int height, Point seedPoint, int tolerance = 10)
        {
            // Validate inputs
            if (grayscalePixels == null || grayscalePixels.Length != width * height)
                throw new ArgumentException("Invalid pixel data or dimensions");
            
            if (seedPoint.X < 0 || seedPoint.X >= width || seedPoint.Y < 0 || seedPoint.Y >= height)
                throw new ArgumentException("Seed point is outside image bounds");
            
            // Initialize result mask - 1 for pixels in region, 0 for others
            var regionMask = new byte[width * height];
            
            // Get the intensity value at the seed point
            int seedIntensity = grayscalePixels[seedPoint.Y * width + seedPoint.X];
            
            // Queue for BFS traversal
            var queue = new Queue<Point>();
            queue.Enqueue(seedPoint);
            
            // Mark seed point as part of the region
            regionMask[seedPoint.Y * width + seedPoint.X] = 1;
            
            // 8-connected neighborhood offsets
            int[] dx = { -1, -1, -1, 0, 0, 1, 1, 1 };
            int[] dy = { -1, 0, 1, -1, 1, -1, 0, 1 };
            
            // Process queue until empty
            while (queue.Count > 0)
            {
                Point current = queue.Dequeue();
                
                // Check all 8 neighbors
                for (int i = 0; i < 8; i++)
                {
                    int newX = current.X + dx[i];
                    int newY = current.Y + dy[i];
                    
                    // Check if neighbor is within image bounds
                    if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                    {
                        int index = newY * width + newX;
                        
                        // Check if pixel hasn't been processed yet
                        if (regionMask[index] == 0)
                        {
                            // Check if pixel intensity is within tolerance
                            int intensity = grayscalePixels[index];
                            if (Math.Abs(intensity - seedIntensity) <= tolerance)
                            {
                                // Add pixel to region
                                regionMask[index] = 1;
                                queue.Enqueue(new Point(newX, newY));
                            }
                        }
                    }
                }
            }
            
            return regionMask;
        }
    }
}