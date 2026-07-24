using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TinyMvcDemo.Models;

namespace TinyMvcDemo.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IWebHostEnvironment _environment;

    public HomeController(ILogger<HomeController> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public IActionResult Index()
    {
        var model = new HomePageViewModel
        {
            AppName = "Tiny MVC Demo",
            DemoMessage = Environment.GetEnvironmentVariable("DEMO_MESSAGE")
                ?? "Sua dong chu nay, push len GitHub, GitHub Actions se tu build image va deploy lai.",
            Commit = Environment.GetEnvironmentVariable("GIT_COMMIT_SHORT") ?? "local-dev",
            BuildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "manual",
            DeployedAt = Environment.GetEnvironmentVariable("DEPLOYED_AT") ?? DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            EnvironmentName = _environment.EnvironmentName
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
