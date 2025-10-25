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

/// <summary>
/// Current Implementation (Upload.cshtml.cs)
/// Uses fo-dicom 4.x and fo-dicom.Drawing for DICOM handling.
/// For DICOM files:
/// Saves the uploaded file to a temp path.
/// Opens the file with DicomFile.Open.
/// Extracts metadata.
/// Renders the image and converts it to a System.Drawing.Bitmap using AsClonedBitmap().
/// Saves the bitmap directly as PNG using bitmap.Save.
/// For standard images (PNG/JPG):
/// Saves the uploaded file directly to the images folder.
/// No conversion to ImageSharp or pixel manipulation.
/// Simple but works well for ONLY Windows.
/// 
/// Limitations:
/// - Relies on System.Drawing.Common, which is Windows-specific and not cross-platform.
/// - No pixel-level manipulation using ImageSharp for DICOM images.
/// - Limited error handling for unsupported DICOM formats.
/// - Does not handle multi-frame DICOM files.
/// 
/// Future Improvements:
/// - Migrate to a fully cross-platform image processing library for DICOM rendering.
/// - Implement pixel-level manipulations using ImageSharp if needed.
/// - Enhance error handling and support for more DICOM modalities.
/// - Add support for multi-frame DICOM files.
/// - Consider using fo-dicom's built-in rendering capabilities instead of converting to System.Drawing.Bitmap.
/// - Add validation for DICOM files (e.g., checking if the file is actually a DICOM file).
/// - Optimize image saving and loading processes.
/// 
/// </summary>

namespace MedicalImageAnalysis.Web.Pages
{
  public class UploadModel : PageModel
  {
    [BindProperty]
    public IFormFile? UploadedFile { get; set; }
    // Represents a file sent with the HttpRequest.

    public string? DisplayImageUrl { get; private set; }
    public string? Modality { get; private set; }
    public string? PatientName { get; private set; }
    private readonly ILogger<UploadModel> _logger;
    public UploadModel(ILogger<UploadModel> logger)
    {
        _logger = logger;
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

      string displayImagePath;

      // DICOM files are unpredictable. They may be corrupted, incomplete, 
      // or use unsupported transfer syntaxes (e.g., JPEG2000 compression)
      try 
      {
        if (fileExtension == ".dcm") {
          _logger.LogInformation("Processing DICOM file: {FileName}", UploadedFile.FileName);

          // Open DICOM from stream
          /*
          var dicomTempPath = Path.Combine(Path.GetTempPath(), baseFileName + ".dcm");
          using (var stream = new FileStream(dicomTempPath, FileMode.Create))
            { await UploadedFile.CopyToAsync(stream); }
          var dicomFile = DicomFile.Open(dicomTempPath); */
          // Open DICOM from stream
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
          var rendered = dicomImage.RenderImage(); // Renders DICOM image to IImage.
          var width = dicomImage.Width;
          var height = dicomImage.Height;

          /* // using Windows System.Drawing.Bitmap
          var pngPath = Path.Combine(uploadsFolder, outputFileName);
          using var bitmap = rendered.AsClonedBitmap();
          bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png); */

          // Work with pixel data using ImageSharp
          var pixelData = dicomImage.RenderImage().Pixels; // Dicom.IO.PinnedIntArray
          
          if (pixelData.Data == null || pixelData.Data.Length == 0)
              throw new InvalidOperationException("DICOM image has no pixel data.");

          // Create ImageSharp image from pixel data
          // Normalize pixel values to 0-255 range for grayscale image
          var pixels = new byte[width * height];
          int min = pixelData.Data.Min();
          int max = pixelData.Data.Max();
          int range = Math.Max(max - min, 1);

          for (int i = 0; i < pixelData.Data.Length; i++)
          {
              pixels[i] = (byte)(((pixelData.Data[i] - min) * 255) / range);
          }

          // Create ImageSharp image
          var image = Image.LoadPixelData<L8>(pixels, width, height);
          
          // Save as PNG
          await image.SaveAsPngAsync(outputFilePath);

        } // Handle standard images (PNG/JPG)
        else if (fileExtension is ".png" or ".jpg" or ".jpeg")
        {
          _logger.LogInformation("Processing standard image: {FileName}", UploadedFile.FileName);
          await using var stream = new FileStream(outputFilePath, FileMode.Create);
          await UploadedFile.CopyToAsync(stream);
        }
        else
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
  }
}