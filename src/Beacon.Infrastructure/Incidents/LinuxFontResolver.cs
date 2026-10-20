using PdfSharp.Fonts;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// The Linux counterpart to WindowsFontResolver, for the container image. Liberation Sans is
/// metric-compatible with Arial, so PDF layout matches what Windows produces. The Beacon.Api
/// Dockerfile installs it (Debian's fonts-liberation package) at the path below.
/// </summary>
public sealed class LinuxFontResolver : IFontResolver
{
    private const string FontDirectory = "/usr/share/fonts/truetype/liberation";

    public byte[] GetFont(string faceName) => faceName switch
    {
        "Arial#Bold" => File.ReadAllBytes(Path.Combine(FontDirectory, "LiberationSans-Bold.ttf")),
        "Arial#Regular" => File.ReadAllBytes(Path.Combine(FontDirectory, "LiberationSans-Regular.ttf")),
        _ => throw new InvalidOperationException($"Unresolved font face: {faceName}")
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(familyName == "Arial" && isBold ? "Arial#Bold" : "Arial#Regular");
}
