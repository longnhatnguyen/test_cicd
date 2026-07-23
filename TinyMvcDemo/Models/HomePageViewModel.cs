namespace TinyMvcDemo.Models;

public class HomePageViewModel
{
    public required string AppName { get; init; }
    public required string DemoMessage { get; init; }
    public required string Commit { get; init; }
    public required string BuildNumber { get; init; }
    public required string DeployedAt { get; init; }
    public required string EnvironmentName { get; init; }
}
