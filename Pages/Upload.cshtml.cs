using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO;
using Dicom;
using Dicom.Imaging;
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
            Directory.CreateDirectory(uploadsFolder);

            var originalFileName = UploadedFile.FileName;
            var fileExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var baseFileName = Guid.NewGuid().ToString();

            string displayImagePath;

            if (fileExtension == ".dcm")
            {
                // Handle DICOM
                var dicomTempPath = Path.Combine(Path.GetTempPath(), baseFileName + ".dcm");
                using (var stream = new FileStream(dicomTempPath, FileMode.Create))
                {
                    await UploadedFile.CopyToAsync(stream);
                }

                try
                {
                    var dicomFile = DicomFile.Open(dicomTempPath);
                    var dataset = dicomFile.Dataset;

                    // Extract metadata
                    PatientName = dataset.GetString(DicomTag.PatientName) ?? "Unknown";
                    Modality = dataset.GetString(DicomTag.Modality) ?? "Unknown";

                    // Render image
                    var image = new DicomImage(dicomFile.Dataset);
                    using var rendered = image.RenderImage();
                    using var bitmap = rendered.AsClonedBitmap(); // System.Drawing.Bitmap


                    // Convert to PNG via ImageSharp
                    var pngPath = Path.Combine(uploadsFolder, baseFileName + ".png");
                    bitmap.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);

                    // using var imageSharp = Image.LoadPixelData<Rgb24>(bitmap.LockBits(), bitmap.Width, bitmap.Height);
                    // await imageSharp.SaveAsPngAsync(pngPath);

                    displayImagePath = pngPath;
                    DisplayImageUrl = $"/images/{baseFileName}.png";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process DICOM file");
                    ModelState.AddModelError(string.Empty, "Invalid or unsupported DICOM file.");
                    return Page();
                }
                finally
                {
                    // Clean up the temporary file
                    if (System.IO.File.Exists(dicomTempPath)) System.IO.File.Delete(dicomTempPath);
                }
            }
            else
            {
                // Handle standard images (PNG/JPG)
                var imagePath = Path.Combine(uploadsFolder, baseFileName + fileExtension);
                using (var stream = new FileStream(imagePath, FileMode.Create))
                {
                    await UploadedFile.CopyToAsync(stream);
                }
                displayImagePath = imagePath;
                DisplayImageUrl = $"/images/{baseFileName}{fileExtension}";
            }
            return Page();
        }

    }
}
