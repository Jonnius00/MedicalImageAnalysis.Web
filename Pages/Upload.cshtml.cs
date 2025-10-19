using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MedicalImageAnalysis.Web.Pages
{
    public class UploadModel : PageModel
    {
        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        public string? ImageUrl { get; private set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (UploadedFile == null || UploadedFile.Length == 0)
                return Page();

            // Save to wwwroot/images (create folder if needed)
            var uploadsFolder = Path.Combine("wwwroot", "images");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = Guid.NewGuid() + Path.GetExtension(UploadedFile.FileName);
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await UploadedFile.CopyToAsync(stream);
            }

            ImageUrl = $"/images/{fileName}";
            return Page();
        }
    }
}