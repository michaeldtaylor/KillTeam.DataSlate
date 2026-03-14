using KillTeam.DataSlate.Domain.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace KillTeam.DataSlate.Console.Services;

/// <summary>
/// Detects weapon type (Ranged or Melee) from a GW Kill Team datacards PDF using
/// PdfPig vector path counting.
///
/// GW's weapon-type icons are vector graphics in the PDF. Each weapon row contains
/// an icon in the left margin (X band 3–35):
///   - Ranged icon (3 stacked bullets): produces 6 paths per row
///   - Melee icon (single dagger):      produces 1 path per row
/// </summary>
public class PdfWeaponTypeDetector
{
    private const double IconXMin = 3.0;
    private const double IconXMax = 35.0;
    private const double RowGroupingUnits = 12.0;
    private const double RowMatchTolerance = 10.0;
    private const double WeaponNameXMin = 20.0;
    private const double WeaponNameXMax = 120.0;
    private const int RangedPathThreshold = 2;

    /// <summary>
    /// Reads a datacards PDF and returns a dictionary mapping weapon name to weapon type.
    /// Weapon names may appear on multiple pages; the last seen value wins (all pages are consistent).
    /// </summary>
    public Dictionary<string, WeaponType> Detect(string pdfPath)
    {
        var result = new Dictionary<string, WeaponType>(StringComparer.OrdinalIgnoreCase);

        using var doc = PdfDocument.Open(pdfPath);

        for (var pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
        {
            var page = doc.GetPage(pageNum);
            var words = page.GetWords().ToList();
            var wordTexts = words.Select(w => w.Text).ToList();

            var hasWeaponTable = wordTexts.Contains("ATK") && wordTexts.Contains("HIT");

            if (hasWeaponTable == false)
            {
                continue;
            }

            var atkWord = words.FirstOrDefault(w => w.Text == "ATK");

            if (atkWord == null)
            {
                continue;
            }

            var tableTopY = atkWord.BoundingBox.Bottom;

            var iconPathRects = new List<PdfRectangle>();

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                try
                {
                    var bb = path.GetBoundingRectangle();

                    if (bb == null)
                    {
                        continue;
                    }

                    var r = bb.Value;

                    if (r.Right < IconXMax && r.Left > IconXMin && r.Top < tableTopY - 2)
                    {
                        iconPathRects.Add(r);
                    }
                }
                catch
                {
                    // Ignore malformed paths
                }
            }

            var rowGroups = iconPathRects
                .GroupBy(r => Math.Round((r.Top + r.Bottom) / 2 / RowGroupingUnits) * RowGroupingUnits)
                .OrderByDescending(g => g.Key);

            foreach (var group in rowGroups)
            {
                var rowY = group.Key;

                var weaponWords = words
                    .Where(w =>
                        w.BoundingBox.Left > WeaponNameXMin &&
                        w.BoundingBox.Left < WeaponNameXMax &&
                        Math.Abs((w.BoundingBox.Top + w.BoundingBox.Bottom) / 2 - rowY) < RowMatchTolerance)
                    .Select(w => w.Text)
                    .ToList();

                if (weaponWords.Count == 0)
                {
                    continue;
                }

                var weaponName = string.Join(" ", weaponWords);
                var weaponType = group.Count() >= RangedPathThreshold ? WeaponType.Ranged : WeaponType.Melee;

                result[weaponName] = weaponType;
            }
        }

        return result;
    }
}
