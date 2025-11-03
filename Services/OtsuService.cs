using System;

namespace MedicalImageAnalysis.Web.Services;

/// <summary>
/// Service for performing Otsu binary thresholding on image data.
/// </summary>
public class OtsuService
{
  public OtsuService()
  { 
  }

  // Apply Otsu thresholding and return binary pixel data
  public byte[] ApplyOtsuThresholding(byte[] grayscalePixels, int width, int height, byte threshold)
  {
    var binaryPixels = new byte[width * height];
    
    for (int i = 0; i < grayscalePixels.Length; i++)
    {
      binaryPixels[i] = grayscalePixels[i] >= threshold ? (byte)255 : (byte)0;
    }

    return binaryPixels;
  }

  // helper for computing OTSU threshold
  public byte ComputeOtsuThreshold(byte[] grayscalePixels)
  {
    const int L = 256; // byte is 0 to 255
    var histogram = new int[L];

    // Build histogram
    foreach (var pixel in grayscalePixels)
      histogram[pixel]++;

    double totalPixels = grayscalePixels.Length;
    double sum = 0;
    for (int i = 0; i < L; i++)
      sum += i * histogram[i];

    double sumB = 0;
    double wB = 0;
    double wF = 0;
    double varMax = 0;
    byte threshold = 0;

    for (int i = 0; i < L; i++) // Changed from byte to int to avoid CS0652 warning
    {
      wB += histogram[i];
      if (wB == 0) continue;

      wF = totalPixels - wB;
      if (wF == 0) break;

      sumB += i * histogram[i];
      double mB = sumB / wB;
      double mF = (sum - sumB) / wF;

      double varBetween = wB * wF * Math.Pow(mB - mF, 2);
      if (varBetween > varMax)
      {
        varMax = varBetween;
        threshold = (byte)i; // Cast to byte when assigning
      }
    }

    return threshold;
  }

}