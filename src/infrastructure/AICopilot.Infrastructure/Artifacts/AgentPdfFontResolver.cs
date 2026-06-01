using PdfSharp.Fonts;

namespace AICopilot.Infrastructure.Artifacts;

internal sealed class AgentPdfFontResolver : IFontResolver
{
    public const string FamilyName = "AICopilotDefault";

    private static readonly object Sync = new();
    private static readonly string FontPath = ResolveFontPath();

    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is not null)
        {
            return;
        }

        lock (Sync)
        {
            GlobalFontSettings.FontResolver ??= new AgentPdfFontResolver();
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(FamilyName);
    }

    public byte[] GetFont(string faceName)
    {
        return File.ReadAllBytes(FontPath);
    }

    private static string ResolveFontPath()
    {
        var windowsFonts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Fonts");
        var candidates = new[]
        {
            Path.Combine(windowsFonts, "NotoSansSC-VF.ttf"),
            Path.Combine(windowsFonts, "msyh.ttc"),
            Path.Combine(windowsFonts, "simhei.ttf"),
            Path.Combine(windowsFonts, "arial.ttf"),
            "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/PingFang.ttc",
            "/System/Library/Fonts/STHeiti Medium.ttc",
            "/System/Library/Fonts/Hiragino Sans GB.ttc",
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new InvalidOperationException("No TrueType/OpenType font is available for PDF artifact generation.");
    }
}
