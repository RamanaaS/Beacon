using PdfSharp.Fonts;

namespace Beacon.Infrastructure.Incidents;

/// <summary>
/// PDFsharp 6's cross-platform core dropped automatic OS font integration that older,
/// GDI+-backed versions had — it now needs an explicit IFontResolver pointed at real font file
/// bytes, or XFont construction throws. Reading straight from the Windows Fonts folder is the
/// honest choice for local dev on Windows; a Linux container (Phase 16+) would need actual font
/// files bundled into the image instead, since C:\Windows\Fonts obviously won't exist there —
/// this class is a real, load-bearing trade-off of "runs on Windows today," not a detail to gloss
/// over when this eventually gets containerized.
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    public byte[] GetFont(string faceName) => faceName switch
    {
        "Arial#Bold" => File.ReadAllBytes(@"C:\Windows\Fonts\arialbd.ttf"),
        "Arial#Regular" => File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf"),
        _ => throw new InvalidOperationException($"Unresolved font face: {faceName}")
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(familyName == "Arial" && isBold ? "Arial#Bold" : "Arial#Regular");
}
