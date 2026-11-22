namespace DroidDesk.Models;

public sealed class InstalledApp
{
    public string PackageName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;

    public string Initial => string.IsNullOrWhiteSpace(DisplayName)
        ? "A"
        : DisplayName.Trim()[0].ToString().ToUpperInvariant();
}
