using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

var files = new[] {
    @"D:\Projects\KillTeam.DataSlate\references\kill-teams\Plague Marines\Plague Marines - Universal Equipment.pdf",
    @"D:\Projects\KillTeam.DataSlate\references\kill-teams\Angels of Death\Angels of Death - Universal Equipment.pdf",
    @"D:\Projects\KillTeam.DataSlate\references\kill-teams\Blades of Khaine\Blades of Khaine - Faction Equipment.pdf",
};

foreach (var path in files)
{
    Console.WriteLine($"\n========== {Path.GetFileName(path)} ==========");
    using var doc = PdfDocument.Open(path);
    
    for (int p = 1; p <= Math.Min(doc.NumberOfPages, 4); p++)
    {
        var page = doc.GetPage(p);
        var words = page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
        
        Console.WriteLine($"\n--- Page {p} ---");
        double lastY = double.MaxValue;
        double lastFontSize = 0;
        var lineWords = new List<string>();
        
        foreach (var word in words)
        {
            var y = Math.Round(word.BoundingBox.Bottom, 1);
            var fontSize = Math.Round(word.Letters[0].PointSize, 1);
            
            if (Math.Abs(y - lastY) > 1 && lineWords.Count > 0)
            {
                var gap = lastY < 1000 ? lastY - y : 0;
                var gapStr = gap > 12 ? $" [GAP: {gap:F1}]" : "";
                var fontChg = lastFontSize != fontSize && lastFontSize > 0 ? $" [FONT: {lastFontSize}->{fontSize}]" : "";
                Console.WriteLine($"Y={lastY,6:F1} {lastFontSize,4:F1}pt  {string.Join(" ", lineWords)}{gapStr}{fontChg}");
                lineWords.Clear();
            }
            
            lastY = y;
            lastFontSize = fontSize;
            lineWords.Add(word.Text);
        }
        if (lineWords.Count > 0)
            Console.WriteLine($"Y={lastY,6:F1} {lastFontSize,4:F1}pt  {string.Join(" ", lineWords)}");
    }
}
