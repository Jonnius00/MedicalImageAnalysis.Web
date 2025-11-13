using System.IO;
using System.Linq;
using Dicom;
using Dicom.Imaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
// using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats; //  Added for ImageSharp
// using SixLabors.ImageSharp.Processing;
using MedicalImageAnalysis.Web.Services; // Added for KMeansService

/// <summary>
/// 
/// Current Implementation uses fo-dicom 5.x and fo-dicom.Drawing for DICOM handling.
/// 
/// For DICOM files:
/// Saves the uploaded file to a temp path.
/// Opens the file with DicomFile.Open.
/// Extracts metadata.
/// Renders the image and
/// NOT YET - converts it to a System.Drawing.Bitmap using AsClonedBitmap().
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
    public IFormFile? UploadedFile { get; set; }     // Represents a file sent with the HttpRequest.

    [BindProperty]
    public string? DisplayImageUrl { get; set; }
    
    [BindProperty]
    public string? OtsuImageUrl { get; set; }
    
    [BindProperty]
    public string? KMeansImageUrl { get; set; } // K-means clustered image
    
    [BindProperty]
    public string? PCAImageUrl { get; set; } // PCA preprocessed image
    
    [BindProperty]
    public string? RegionGrowingImageUrl { get; set; } // region growing segmented image
    
    public double[]? ExplainedVarianceRatios { get; private set; } // PCA explained variance ratios
    
    [BindProperty]
    public string? WatershedImageUrl { get; set; } // watershed segmented image
    
    [BindProperty]
    public int KMeans_K { get; set; } = 3;      // Number of clusters for K-means, default to 3
    
    [BindProperty]
    public int PCAComponents { get; set; } = 2; // Number of PCA components, default to 2
    
    [BindProperty]
    public int RegionGrowingTolerance { get; set; } = 50; // Intensity difference threshold for region growing, default to 10
    
    public string? Modality { get; private set; } 
    public string? PatientName { get; private set; }
    public int NumberOfFrames { get; private set; } = 1; // Number of frames in DICOM file

    private readonly ILogger<UploadModel> _logger;
    private readonly OtsuService _otsuService;            // Otsu service instance
    private readonly KMeansService _kMeansService;        // K-means service instance
    private readonly PCAPreprocessingService _pcaService; // PCA preprocessing service instance
    private readonly RegionGrowingService _regionGrowingService; // Region growing service instance
    private readonly WatershedService _watershedService; // Watershed service instance

    private readonly string _baseFileName;    // shared GUID file name for cached images

    public UploadModel(ILogger<UploadModel> logger, OtsuService otsuService, KMeansService kMeansService, PCAPreprocessingService pcaService, RegionGrowingService regionGrowingService, WatershedService watershedService)
    {
      _logger = logger;
      _otsuService = otsuService;
      _kMeansService = kMeansService;
      _pcaService = pcaService;
      _regionGrowingService = regionGrowingService;
      _watershedService = watershedService;
      _baseFileName = GetBaseFileName();
    }

    #region Helper methods
    
    /// <summary>
    /// Extracts pixel data from an image
    /// </summary>
    /// <param name="originalImage"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <returns>Byte array of pixel data</returns>
    private byte[] ExtractPixelDataFromImage(Image<L8> originalImage, int width, int height)
    {
      var pixels = new byte[width * height];
      originalImage.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          var row = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          { // Get the pixel value
            pixels[y * width + x] = row[x].PackedValue; 
          }
        }
      });
      
      return pixels;
    }

    /// <summary>
    /// Helper extracts the base filename (GUID) from DisplayImageUrl
    /// </summary>
    private string GetBaseFileName()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl))
      {
        // Extract the filename part from the URL
        var fileName = Path.GetFileName(DisplayImageUrl);
        // Remove the extension to get the base name
        return Path.GetFileNameWithoutExtension(fileName);
      }

      // Return a new GUID if DisplayImageUrl is not set
      return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Saves a processed image and returns the URL to access it
    /// </summary>
    /// <param name="image">The image to save</param>
    /// <param name="fileName">output filename</param>
    /// <param name="fileExtension">optional file extension (without dot)</param>
    /// <returns>URL to the saved image</returns>
    private async Task<string> SaveProcessedImageAsync(Image image, string fileName, string fileExtension = "png")
    {
      fileName = $"{fileName}.{fileExtension}";
      var filePath = Path.Combine("wwwroot", "images", fileName);
      
      // Save as PNG (lossless format suitable for medical images)
      await image.SaveAsPngAsync(filePath);
      
      return $"/images/{fileName}";
      // return $"{filePath}.{fileExtension}";
    }

    #endregion

    // All Razor Page models should have an OnGet() method, even empty one
    // serves all GET requests, entry point for initializing the page
    public void OnGet() { } 

    public async Task<IActionResult> OnPostAsync()
    {
      if (UploadedFile == null || UploadedFile.Length == 0) return Page();

      // Creates (sub)directories in specified path unless they already exist.
      var uploadsFolder = Path.Combine("wwwroot", "images");
      Directory.CreateDirectory(uploadsFolder);

      var originalFileName = UploadedFile.FileName;
      var fileExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
      var outputFileName = _baseFileName + ".png";
      var outputFilePath = Path.Combine(uploadsFolder, outputFileName);

      // DICOM files are unpredictable. They may be corrupted, incomplete, 
      // or use unsupported transfer syntaxes (e.g., JPEG2000 compression)
      try
      {
        if (fileExtension == ".dcm")
        {
          _logger.LogInformation("Uploading DICOM file: {FileName}", UploadedFile.FileName);

          /* // Open DICOM from stream. Option 1
          // _baseFileName = Guid.NewGuid().ToString();
          var dicomTempPath = Path.Combine(Path.GetTempPath(), _baseFileName + ".dcm");
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
          
          // Check for number of frames
          NumberOfFrames = dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
          _logger.LogInformation("DICOM file has {NumberOfFrames} frames", NumberOfFrames);

          /* // Extract metadata DEPRECATED way uses .GetSequence(DicomTag.PatientName)
          PatientName = dataset.Get<string>(DicomTag.PatientName, "Anonymous");
          Modality = dataset.Get<string>(DicomTag.Modality, "Unknown"); */

          var dicomImage = new DicomImage(dataset);
          var width = dicomImage.Width;
          var height = dicomImage.Height;

          /* // using WINDOWS System.Drawing.Bitmap
          var rendered = dicomImage.RenderImage(); // Renders DICOM image to IImage.
          using var bitmap = rendered.AsClonedBitmap();
          bitmap.Save(outputFilePath, System.Drawing.Imaging.ImageFormat.Png); */

          // Works with pixel data using ImageSharp, e.g DIRECT PIXEL ACCESS
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

          // Recreates SixLabors.ImageSharp.Image from the pixel data
          var imageSharp = Image.LoadPixelData<L8>(grayscalePixels, width, height);

          // Save as PNG in wwwroot/images
          DisplayImageUrl = await SaveProcessedImageAsync(imageSharp, _baseFileName);

        } // Handle standard images (PNG/JPG)
        else if (fileExtension is ".png" or ".jpg" or ".jpeg")
        {
          _logger.LogInformation("Uploading standard image: {FileName}", UploadedFile.FileName);
          await using var stream = new FileStream(outputFilePath, FileMode.Create);
          await UploadedFile.CopyToAsync(stream);
          DisplayImageUrl = $"/images/{outputFilePath}";
        }
        else // Unsupported file format
        {
          ModelState.AddModelError(string.Empty, "Unsupported file format. Please upload DICOM, PNG, or JPG.");
          return Page();
        }

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

    // Separate OnPostApplyOtsuAsync handler
    public async Task<IActionResult> OnPostApplyOtsuAsync()
    {
      // Reload original image from DisplayImageUrl
      if (!string.IsNullOrEmpty(DisplayImageUrl))
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract byte[] of pixel data from the image
        var grayscalePixels = ExtractPixelDataFromImage(originalImage, width, height);

        // Apply Otsu
        byte otsuThreshold = _otsuService.ComputeOtsuThreshold(grayscalePixels);
        byte[] binaryPixels = _otsuService.ApplyOtsuThresholding(grayscalePixels, width, height, otsuThreshold);
        
        // Create binary image from binary pixel data
        using var binaryImage = Image.LoadPixelData<L8>(binaryPixels, width, height);

        // Save binary result
        OtsuImageUrl = await SaveProcessedImageAsync(binaryImage, _baseFileName + "_otsu");
      }

      return Page();
    }

    #region KMeans Clustering
    // Handler for applying K-means clustering
    public async Task<IActionResult> OnPostApplyKMeansAsync()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl) && KMeans_K > 1)
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract byte[]pixel data from the image
        var grayscalePixels = ExtractPixelDataFromImage(originalImage, width, height);

        // Apply K-means clustering
        var labels = _kMeansService.ApplyKMeans(grayscalePixels, width, height, KMeans_K);

        // Create a colorized image based on cluster labels
        using var clusteredImage = CreateColorizedClusteredImage(labels, width, height, KMeans_K);

        // Save clustered result
        KMeansImageUrl = await SaveProcessedImageAsync(clusteredImage, _baseFileName + "_kmeans");
      }

      return Page();
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
        if (i < predefinedColors.Length) // pick from predefined colors
        { 
          colors[i] = predefinedColors[i];
        }
        else // Generate random color if run out of predefined ones
        { 
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
    #endregion

    // Handler for applying PCA preprocessing
    public async Task<IActionResult> OnPostApplyPCAAsync()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl) && PCAComponents > 0)
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract byte[]pixel data from the image
        var grayscalePixels = ExtractPixelDataFromImage(originalImage, width, height);

        // Apply PCA preprocessing
        var pcaPixels = _pcaService.ApplyPCA(grayscalePixels, width, height, PCAComponents);

        // Compute explained variance ratios
        ExplainedVarianceRatios = _pcaService.ComputeExplainedVarianceRatio(grayscalePixels, width, height);

        // Create image from PCA processed pixels
        using var pcaProcessedImage = Image.LoadPixelData<L8>(pcaPixels, width, height);

        // Save PCA result
        PCAImageUrl = await SaveProcessedImageAsync(pcaProcessedImage, _baseFileName + "_pca");
      }

      return Page();
    }

    #region Region Growing
    // Handler for applying region growing segmentation
    public async Task<IActionResult> OnPostApplyRegionGrowingAsync()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl) && RegionGrowingTolerance >= 0)
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract byte[]pixel data from the image
        var grayscalePixels = ExtractPixelDataFromImage(originalImage, width, height);

        // Define seed point at image center
        var seedPoint = new System.Drawing.Point(width / 2, height / 2);

        // Apply region growing segmentation
        var regionMask = _regionGrowingService.ApplyRegionGrowing(grayscalePixels, width, height, seedPoint, RegionGrowingTolerance);

        // Create segmented image based on region mask
        using var segmentedImage = CreateBinarySegmentedImage(regionMask, width, height);

        // Save region growing result
        RegionGrowingImageUrl = await SaveProcessedImageAsync(segmentedImage, _baseFileName + "_regiongrowing");
      }

      return Page();
    }

    /// <summary>
    /// Creates a binary image based on region mask.
    /// All logic handles Image objects is placed in model file
    /// </summary>
    private Image<L8> CreateBinarySegmentedImage(byte[] regionMask, int width, int height)
    {
      var image = new Image<L8>(width, height);

      image.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          var row = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          {
            int index = y * width + x;
            // Set pixel to white if in region, black otherwise
            row[x] = new L8(regionMask[index] > 0 ? (byte)255 : (byte)0);
          }
        }
      });

      return image;
    }
    #endregion
    
    #region Watershed Segmentation
    // Handler for applying watershed segmentation
    public async Task<IActionResult> OnPostApplyWatershedAsync()
    {
      if (!string.IsNullOrEmpty(DisplayImageUrl))
      {
        var imagePath = Path.Combine("wwwroot", DisplayImageUrl.TrimStart('/'));
        using var originalImage = await Image.LoadAsync<L8>(imagePath);
        var width = originalImage.Width;
        var height = originalImage.Height;

        // Extract byte[]pixel data from the image
        var grayscalePixels = ExtractPixelDataFromImage(originalImage, width, height);

        // Apply watershed segmentation
        var labels = _watershedService.ApplyWatershed(grayscalePixels, width, height);

        // Create a colorized image based on watershed labels
        using var watershedImage = CreateColorizedWatershedImage(labels, width, height);

        // Save watershed result
        WatershedImageUrl = await SaveProcessedImageAsync(watershedImage, _baseFileName + "_watershed");
      }

      return Page();
    }
    
    /// <summary>
    /// Creates a colorized image based on watershed labels.
    /// </summary>
    private Image<Rgba32> CreateColorizedWatershedImage(int[] labels, int width, int height)
    {
      // Find the number of unique labels
      var uniqueLabels = labels.Distinct().OrderBy(x => x).ToArray();
      int maxLabel = uniqueLabels.Length > 0 ? uniqueLabels.Max() : 0;
      
      // Create color map for labels
      var random = new Random(42); // Fixed seed for reproducibility
      var colorMap = new Dictionary<int, Rgba32>();
      
      // Assign colors to labels
      foreach (int label in uniqueLabels)
      {
        if (label == 0)
        {
          // Background - black
          colorMap[label] = new Rgba32(0, 0, 0);
        }
        else if (label == -1)
        {
          // Watershed lines - red
          colorMap[label] = new Rgba32(255, 0, 0);
        }
        else
        {
          // Generate random color for other regions
          byte r = (byte)random.Next(0, 256);
          byte g = (byte)random.Next(0, 256);
          byte b = (byte)random.Next(0, 256);
          colorMap[label] = new Rgba32(r, g, b);
        }
      }

      // Create colorized image
      var colorizedImage = new Image<Rgba32>(width, height);
      colorizedImage.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          var row = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          {
            int index = y * width + x;
            int label = labels[index];
            
            if (colorMap.ContainsKey(label))
            {
              row[x] = colorMap[label];
            }
            else
            {
              // Default to white if label not found
              row[x] = new Rgba32(255, 255, 255);
            }
          }
        }
      });

      return colorizedImage;
    }
    #endregion

  }
}