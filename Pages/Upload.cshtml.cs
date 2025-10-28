using System.IO;
using System.Linq;
using Dicom;
using Dicom.Imaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using MedicalImageAnalysis.Web.Services; // Added for KMeansService

/// <summary>
/// 
/// Current Implementation (Upload.cshtml.cs)
/// Uses fo-dicom 5.x and fo-dicom.Drawing for DICOM handling.
/// 
/// For DICOM files:
/// Saves the uploaded file to a temp path.
/// Opens the file with DicomFile.Open.
/// Extracts metadata.
/// Renders the image and converts it to a System.Drawing.Bitmap using AsClonedBitmap().
/// Saves the bitmap directly as PNG.
///  
/// For standard images (PNG/JPG):
/// Saves the uploaded file directly to the images folder.
/// 
/// </summary>

namespace MedicalImageAnalysis.Web.Pages
{
  public class UploadModel : PageModel
  {
    [BindProperty]
    public IFormFile? UploadedFile { get; set; }
    // Represents a file sent with the HttpRequest.

    [BindProperty]
    public string? DisplayImageUrl { get; set; }
    
    public string? OtsuImageUrl { get; private set; }
    
    public string? KMeansImageUrl { get; private set; } // For K-means clustered image
    
    [BindProperty]
    public int KMeans_K { get; set; } = 3;      // Number of clusters for K-means, default to 3
    
    public string? Modality { get; private set; } 
    public string? PatientName { get; private set; }
    private readonly ILogger<UploadModel> _logger;
    private readonly KMeansService _kMeansService;      // K-means service instance
    
    public UploadModel(ILogger<UploadModel> logger)
    {
        _logger = logger;
        _kMeansService = new KMeansService(); // Initialize K-means service
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
      if (UploadedFile == null || UploadedFile.Length == 0) return Page();

      var uploadsFolder = Path.Combine("wwwroot", "images");
      // Creates (sub)directories in specified path unless they already exist.
      Directory.CreateDirectory(uploadsFolder);

      var originalFileName = UploadedFile.FileName;
      var fileExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
      // var baseFileName = Guid.NewGuid(); // .ToString();
      var outputFileName = Guid.NewGuid() + ".png";
      var outputFilePath = Path.Combine(uploadsFolder, outputFileName);

      // DICOM files are unpredictable. They may be corrupted, incomplete, 
      // or use unsupported transfer syntaxes (e.g., JPEG2000 compression)
      try
      {
        if (fileExtension == ".dcm")
        {
          _logger.LogInformation("Uploading DICOM file: {FileName}", UploadedFile.FileName);

          // Open DICOM from stream. Option 1
          /*
          var dicomTempPath = Path.Combine(Path.GetTempPath(), baseFileName + ".dcm");
          using (var stream = new FileStream(dicomTempPath, FileMode.Create))
            { await UploadedFile.CopyToAsync(stream); }
          var dicomFile = DicomFile.Open(dicomTempPath); */

          // Open DICOM from stream. Option 2
          await using var stream = UploadedFile.OpenReadStream();
          var dicomFile = await DicomFile.OpenAsync(stream);

          var dataset = dicomFile.Dataset;

          // Extract metadata
          PatientName = dataset.GetString(DicomTag.PatientName) ?? "Unknown";
          Modality = dataset.GetString(DicomTag.Modality) ?? "Unknown";

          // Extract metadata DEPRECATED, use .GetSequence(DicomTag.PatientName)
          // PatientName = dataset.Get<string>(DicomTag.PatientName, "Anonymous");
          // Modality = dataset.Get<string>(DicomTag.Modality, "Unknown");

          var dicomImage = new DicomImage(dataset);
          var width = dicomImage.Width;
          var height = dicomImage.Height;

          /* // using Windows System.Drawing.Bitmap
          var pngPath = Path.Combine(uploadsFolder, outputFileName);
          var rendered = dicomImage.RenderImage(); // Renders DICOM image to IImage.
          using var bitmap = rendered.AsClonedBitmap();
          bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png); */

          // Work with pixel data using ImageSharp, e.g DIRECT PIXEL ACCESS
          var pixelData = dicomImage.RenderImage().Pixels; // Dicom.IO.PinnedIntArray

          if (pixelData.Data == null || pixelData.Data.Length == 0)
            throw new InvalidOperationException("DICOM image has no pixel data.");

          // Normalize pixel values to 0-255 range for grayscale image (required for display)
          var grayscalePixels = new byte[width * height];
          int min = pixelData.Data.Min();
          int max = pixelData.Data.Max();
          int range = Math.Max(max - min, 1);
          for (int i = 0; i < pixelData.Data.Length; i++)
          {
            grayscalePixels[i] = (byte)(((pixelData.Data[i] - min) * 255) / range);
          }

          // Create ImageSharp image from pixel data
          var imageSharp = Image.LoadPixelData<L8>(grayscalePixels, width, height);

/*        #region OTSU processing // TODO: put in a separate function
          // Compute Otsu threshold
          byte otsuThreshold = ComputeOtsuThreshold(grayscalePixels);

          // Create binary image
          using var binaryImage = ApplyOtsuThresholding(grayscalePixels, width, height, otsuThreshold);

          // Save binary result
          var otsuFileName = Guid.NewGuid() + "_otsu.png";
          var otsuPath = Path.Combine("wwwroot", "images", otsuFileName);
          await binaryImage.SaveAsPngAsync(otsuPath);
          OtsuImageUrl = $"/images/{otsuFileName}"; // set route to Otsu processed file
          #endregion OTSU processing */

          // Save as PNG in wwwroot/images
          await imageSharp.SaveAsPngAsync(outputFilePath);

        } // Handle standard images (PNG/JPG)
        else if (fileExtension is ".png" or ".jpg" or ".jpeg")
        {
          _logger.LogInformation("Uploading standard image: {FileName}", UploadedFile.FileName);
          await using var stream = new FileStream(outputFilePath, FileMode.Create);
          await UploadedFile.CopyToAsync(stream);
        }
        else // Unsupported file format
        {
          ModelState.AddModelError(string.Empty, "Unsupported file format. Please upload DICOM, PNG, or JPG.");
          return Page();
        }

        DisplayImageUrl = $"/images/{outputFileName}";
        _logger.LogInformation("Image saved successfully: {Path}", outputFilePath);
      }
      catch (DicomFileException ex)
      {
        _logger.LogError(ex, "Invalid DICOM file: {FileName}", UploadedFile?.FileName);
        ModelState.AddModelError(string.Empty, "The uploaded file is not a valid DICOM image.");
      }
      catch (IOException ex)
      {
        _logger.LogError(ex, "I/O error while saving image.");
        ModelState.AddModelError(string.Empty, "Failed to save the uploaded file. Please try again.");
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Unexpected error during image processing.");
        ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try a different file.");
      }

      return Page();
    }

    // Separate OnPostApplyOtsuAsync handler (more interactive, needs image caching)
    public async Task<IActionResult> OnPostApplyOtsuAsync()
    {
      // Reload original image from DisplayImageUrl
      if (!string.IsNullOrEmpty(DisplayImageUrl)) // DisplayImageUrl handling nullable
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract pixel data from the image
        var grayscalePixels = new byte[width * height];
        originalImage.ProcessPixelRows(accessor =>
        {
          for (int y = 0; y < height; y++)
          {
            var row = accessor.GetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
              grayscalePixels[y*width + x] = row[x].PackedValue; // Get the pixel value
            }
          }
        });

        // Apply Otsu
        byte otsuThreshold = ComputeOtsuThreshold(grayscalePixels);
        using var binaryImage = ApplyOtsuThresholding(grayscalePixels, width, height, otsuThreshold);

        // Save binary result
        var otsuFileName = Guid.NewGuid() + "_otsu.png";
        var otsuPath = Path.Combine("wwwroot", "images", otsuFileName);
        await binaryImage.SaveAsPngAsync(otsuPath);
        OtsuImageUrl = $"/images/{otsuFileName}";
      }

      return Page();
    }
    
    // Handler for applying K-means clustering
    public async Task<IActionResult> OnPostApplyKMeansAsync()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl) && KMeans_K > 1)
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract pixel data from the image
        var grayscalePixels = new byte[width * height];
        originalImage.ProcessPixelRows(accessor =>
        {
          for (int y = 0; y < height; y++)
          {
            var row = accessor.GetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
              grayscalePixels[y * width + x] = row[x].PackedValue;
            }
          }
        });

        // Apply K-means clustering
        var labels = _kMeansService.ApplyKMeans(grayscalePixels, width, height, KMeans_K);
        
        // Create a colorized image based on cluster labels
        using var clusteredImage = CreateColorizedClusteredImage(labels, width, height, KMeans_K);
        
        // Save clustered result
        var kmeansFileName = Guid.NewGuid() + "_kmeans.png";
        var kmeansPath = Path.Combine("wwwroot", "images", kmeansFileName);
        await clusteredImage.SaveAsPngAsync(kmeansPath);
        KMeansImageUrl = $"/images/{kmeansFileName}";
      }
      
      return Page();
    }

    private Image<Rgba32> ApplyOtsuThresholding(byte[] grayscalePixels, int width, int height, byte threshold)
    {
      // Create binary image
      var binaryImage = new Image<Rgba32>(width, height);
      binaryImage.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          var row = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          {
            byte gray = grayscalePixels[y*width + x];
            byte binary = gray >= threshold ? (byte)255 : (byte)0;
            row[x] = new Rgba32(binary, binary, binary, 255);
          }
        }
      });
      
      return binaryImage;
    }
    
    /// <summary>
    /// Creates a colorized image based on cluster labels.
    /// </summary>
    private Image<Rgba32> CreateColorizedClusteredImage(int[] labels, int width, int height, int k)
    {
      var image = new Image<Rgba32>(width, height);
      
      // Define distinct colors for each cluster
      var colors = GenerateDistinctColors(k);
      
      image.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          var row = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          {
            int index = y * width + x;
            row[x] = colors[labels[index]];
          }
        }
      });
      
      return image;
    }
    
    /// <summary>
    /// Generates distinct colors for cluster visualization.
    /// </summary>
    private Rgba32[] GenerateDistinctColors(int count)
    {
      var colors = new Rgba32[count];
      
      // Predefined set of distinct colors
      var predefinedColors = new[]
      {
        new Rgba32(255, 0, 0),    // Red
        new Rgba32(0, 255, 0),    // Green
        new Rgba32(0, 0, 255),    // Blue
        new Rgba32(255, 255, 0),  // Yellow
        new Rgba32(255, 0, 255),  // Magenta
        new Rgba32(0, 255, 255),  // Cyan
        new Rgba32(255, 128, 0),  // Orange
        new Rgba32(128, 0, 255),  // Purple
      };
      
      for (int i = 0; i < count; i++)
      {
        if (i < predefinedColors.Length)
        {
          colors[i] = predefinedColors[i];
        }
        else
        {
          // Generate random color if we run out of predefined ones
          var random = new Random(42 + i); // Use a fixed seed for consistency
          colors[i] = new Rgba32(
            (byte)random.Next(256),
            (byte)random.Next(256),
            (byte)random.Next(256)
          );
        }
      }
      
      return colors;
    }

    private byte ComputeOtsuThreshold(byte[] grayscalePixels)
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
}