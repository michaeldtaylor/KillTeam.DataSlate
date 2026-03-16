using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

var path = args[0];
using var doc = PdfDocument.Open(path);
foreach (var page in doc.GetPages())
{
    var text = ContentOrderTextExtractor.GetText(page);
    var lines = text.Split('\n');
    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        var display = string.IsNullOrWhiteSpace(line) ? "[BLANK]" : line;
        Console.WriteLine($"{i:D3}: {display}");
    }
}
