using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;

namespace TinyMvcDemo.Controllers;

public class DownloadController : Controller
{
    private readonly IWebHostEnvironment _environment;
    private static readonly Dictionary<string, string[]> SourceFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = new[]
        {
            "LONGNHATNGUYEN_CAU1.html",
            "LONGNHATNGUYEN_CAU2_DANG1.html",
            "LONGNHATNGUYEN_CAU2.html",
            "README_SOURCE.txt"
        },
        ["cau1"] = new[] { "LONGNHATNGUYEN_CAU1.html", "README_SOURCE.txt" },
        ["cau2-dang1"] = new[] { "LONGNHATNGUYEN_CAU2_DANG1.html", "README_SOURCE.txt" },
        ["cau2-dang2"] = new[] { "LONGNHATNGUYEN_CAU2.html", "README_SOURCE.txt" }
    };

    public DownloadController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Source(string id = "all")
    {
        var sourceDirectory = Path.Combine(_environment.WebRootPath, "ontap");
        var files = SourceFiles.TryGetValue(id, out var selectedFiles)
            ? selectedFiles
            : SourceFiles["all"];

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

        return File(stream.ToArray(), "application/zip", GetDownloadName(id));
    }

    private static string GetDownloadName(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "cau1" => "LONGNHATNGUYEN_CAU1_SOURCE.zip",
            "cau2-dang1" => "LONGNHATNGUYEN_CAU2_DANG1_SOURCE.zip",
            "cau2-dang2" => "LONGNHATNGUYEN_CAU2_DANG2_SOURCE.zip",
            _ => "LONGNHATNGUYEN_ONTAP_LTW_2026_SOURCE.zip"
        };
    }
}
