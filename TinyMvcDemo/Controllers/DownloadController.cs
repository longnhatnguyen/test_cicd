using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;

namespace TinyMvcDemo.Controllers;

public class DownloadController : Controller
{
    private readonly IWebHostEnvironment _environment;

    public DownloadController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Source()
    {
        var sourceDirectory = Path.Combine(_environment.WebRootPath, "ontap");
        var files = new[]
        {
            "LONGNHATNGUYEN_CAU1.html",
            "LONGNHATNGUYEN_CAU2.html",
            "README_SOURCE.txt"
        };

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var path = Path.Combine(sourceDirectory, file);
                if (!System.IO.File.Exists(path))
                {
                    continue;
                }

                archive.CreateEntryFromFile(path, file);
            }
        }

        return File(stream.ToArray(), "application/zip", "LONGNHATNGUYEN_ONTAP_LTW_2026_SOURCE.zip");
    }
}
