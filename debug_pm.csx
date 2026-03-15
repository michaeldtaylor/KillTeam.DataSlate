using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

var psi = new ProcessStartInfo {
    FileName = "pdftotext",
    RedirectStandardOutput = true,
    UseShellExecute = false,
    StandardOutputEncoding = Encoding.UTF8,
};
psi.ArgumentList.Add("-raw");
psi.ArgumentList.Add(@"references\kill-teams\Plague Marines\Plague Marines - Datacards.pdf");
psi.ArgumentList.Add("-");

using var proc = Process.Start(psi)!;
var lines = new List<string>();
while (proc.StandardOutput.ReadLine() is { } line) lines.Add(line.Replace("\f",""));
proc.WaitForExit();

Console.WriteLine("Total lines: " + lines.Count);
var wtRegex = new Regex(@"\bNAME\b.*\bATK\b.*\bHIT\b.*\bDMG\b");
var capsRegex = new Regex(@"^[A-Z][A-Z\s\-]+$");
var statsKw = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "SAVE","MOVE","WOUNDS","APL","APL WOUNDS","APL MOVE","MOVE SAVE","WOUNDS SAVE","APL MOVE SAVE WOUNDS","OPERATIVES" };
for (int i = 0; i < lines.Count; i++) {
    var l = lines[i]; var t = l.Trim();
    bool cont = t.Contains("CONTINUE ON OTHER SIDE", StringComparison.OrdinalIgnoreCase);
    bool wt = !cont && wtRegex.IsMatch(l);
    bool caps = capsRegex.IsMatch(t) && !t.Contains(',') && !statsKw.Contains(t);
    if (cont||wt||caps) Console.WriteLine("Line {0} [{1}]: [{2}]", i, cont?"CONT":wt?"WT":"CAPS", t);
}
