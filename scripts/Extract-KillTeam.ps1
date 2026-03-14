#Requires -Version 7.0
<#
.SYNOPSIS
    Extracts Kill Team operative data from official GW PDF sources and writes validated JSON.

.DESCRIPTION
    Reads 'Datacards.pdf' (operatives, stats and weapons) and equipment PDFs from
    references/kill-teams/<TeamName>/ and writes a schema-valid JSON file to teams/<slug>.json.

    Requires Poppler's pdftotext to be installed:
        winget install oschwartz10612.Poppler

.EXAMPLE
    .\Extract-KillTeam.ps1 -TeamName "Blades of Khaine"
    .\Extract-KillTeam.ps1 -All
    .\Extract-KillTeam.ps1 -TeamName "Angels of Death" -Force
#>

[CmdletBinding(DefaultParameterSetName = 'Single')]
param(
    [Parameter(ParameterSetName = 'Single', Mandatory)]
    [string]$TeamName,

    [Parameter(ParameterSetName = 'All')]
    [switch]$All,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot    = Split-Path -Parent $PSScriptRoot
$ReferencesBase = Join-Path $ProjectRoot 'references\kill-teams'
$TeamsOut       = Join-Path $ProjectRoot 'teams'
$SchemaPath     = Join-Path $ProjectRoot 'schema\team.schema.json'

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------

function Assert-PdfToText {
    if (-not (Get-Command pdftotext -ErrorAction SilentlyContinue)) {
        throw "pdftotext not found on PATH.`n" +
              "Install Poppler:  winget install oschwartz10612.Poppler`n" +
              "Then restart your shell."
    }
}

# ---------------------------------------------------------------------------
# Text helpers
# ---------------------------------------------------------------------------

function ConvertTo-TitleCase([string]$text) {
    return (Get-Culture).TextInfo.ToTitleCase($text.ToLower())
}

function Get-PdfLines([string]$pdfPath) {
    $raw = & pdftotext -layout $pdfPath - 2>$null
    # Strip embedded form-feed characters that appear as card-page separators
    return @($raw | ForEach-Object { $_ -replace '\f', '' })
}

# ---------------------------------------------------------------------------
# Weapon-type detection (Ranged vs Melee inferred from name and rules)
# ---------------------------------------------------------------------------

$script:RangedKeywords = @(
    'rifle', 'pistol', 'bolter', 'flamer', 'launcher', 'cannon', 'shotgun',
    'blaster', 'carbine', 'gun', 'las', 'sniper', 'melta', 'autocannon',
    'stubber', 'boltgun', 'plasma', 'volkite', 'grenade', 'burst', 'thrower', 'arc',
    # Aeldari ranged weapons (no "Range" in WR column for some)
    'shuriken', 'catapult', 'avenger', 'death spinner', 'reaper launcher',
    'scatter laser', 'starcannon', 'wraithcannon'
)

# Melee words take priority — checked BEFORE ranged keywords to handle names like "gun butts"
$script:MeleeOverrideKeywords = @(
    'butt', 'fist', 'claw', 'talon', 'blade', 'sword', 'maul', 'hammer',
    'staff', 'spear', 'axe', 'dagger', 'knife', 'bite', 'tentacle',
    'cleaver', 'scythe', 'trident', 'mace', 'flail', 'whip', 'gauntlet', 'knuckle'
)

function Get-WeaponType([string]$name, [string]$rules) {
    $lower = $name.ToLower()

    # Melee-override keywords take priority over ranged detection
    foreach ($kw in $script:MeleeOverrideKeywords) {
        if ($lower.Contains($kw)) {
            return 'Melee'
        }
    }

    foreach ($kw in $script:RangedKeywords) {
        if ($lower.Contains($kw)) {
            return 'Ranged'
        }
    }

    if ($rules -match '\bRange\b') {
        return 'Ranged'
    }

    return 'Melee'
}

# ---------------------------------------------------------------------------
# Equipment parser
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Equipment parser
# ---------------------------------------------------------------------------

$script:SectionHeaders = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@('FACTION EQUIPMENT', 'UNIVERSAL EQUIPMENT'),
    [StringComparer]::Ordinal
)

$script:EquipSkip = @(
    '^RULE(S)? CONTINUES? ON OTHER SIDE$',
    # Action names carry an AP cost on the same line, e.g. "MOVE WITH BARRICADE 1AP"
    '.*\d+AP$',
    # PDF table column headers that appear in the explosive-grenades section
    '^(NAME|ATK|HIT|DMG|WR|APL|MOVE|SAVE|WOUNDS)$'
)

function Test-IsEquipmentSkip([string]$text) {
    foreach ($p in $script:EquipSkip) {
        if ($text -cmatch $p) {
            return $true
        }
    }

    return $false
}

function Parse-Equipment([string[]]$pdfPaths, [string]$teamName = '') {
    $result = [System.Collections.Generic.List[string]]::new()
    $seen   = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($pdfPath in $pdfPaths) {
        $allLines = @(Get-PdfLines $pdfPath)
        $total    = $allLines.Count

        for ($j = 0; $j -lt $total; $j++) {
            $trimmed = $allLines[$j].Trim()

            if ($trimmed.Length -lt 4) {
                continue
            }

            # Skip section header lines themselves
            if ($script:SectionHeaders.Contains($trimmed)) {
                continue
            }

            # CASE-SENSITIVE all-uppercase check (letters, spaces, hyphens, apostrophes,
            # optional leading quantity prefix like "1X " or "2X ")
            if ($trimmed -cnotmatch "^(\d+X\s+)?[A-Z][A-Z\s\-']+$") {
                continue
            }

            # Skip weapon-table column-header lines
            if ($trimmed -cmatch '\bATK\b' -and $trimmed -cmatch '\bHIT\b') {
                continue
            }

            if (Test-IsEquipmentSkip $trimmed) {
                continue
            }

            # If the next non-blank line is a section header, this ALL-CAPS line is a page
            # title (e.g. team name repeating on each card), not an equipment item.
            $nextNonBlank = ''
            for ($k = $j + 1; $k -lt $total; $k++) {
                $kLine = $allLines[$k].Trim()

                if ($kLine.Length -gt 0) {
                    $nextNonBlank = $kLine
                    break
                }
            }

            if ($script:SectionHeaders.Contains($nextNonBlank)) {
                continue
            }

            $itemName = $trimmed -replace '^\d+X\s+', ''

            if ($itemName.Length -lt 4) {
                continue
            }

            $display = ConvertTo-TitleCase $itemName

            if ($seen.Add($display)) {
                $result.Add($display)
            }
        }
    }

    return $result
}

# ---------------------------------------------------------------------------
# Datacard parser — extracts operatives, stats, weapons and faction keyword
# ---------------------------------------------------------------------------

function Parse-Datacards([string]$pdfPath) {
    $lines    = Get-PdfLines $pdfPath
    $count    = $lines.Count
    $operatives = [System.Collections.Generic.List[hashtable]]::new()
    $processed  = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $faction    = $null

    $i = 0

    while ($i -lt $count) {
        # ── Locate the stats-header line ────────────────────────────────────
        if ($lines[$i] -notmatch '\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b') {
            $i++
            continue
        }

        # ── Operative name (first non-blank line after header) ───────────────
        $i++

        while ($i -lt $count -and [string]::IsNullOrWhiteSpace($lines[$i])) {
            $i++
        }

        if ($i -ge $count) {
            break
        }

        $nameRaw = $lines[$i].Trim()

        if ($nameRaw -notmatch '^[A-Z][A-Z\s\-]+$') {
            $i++
            continue
        }

        $operativeName = ConvertTo-TitleCase $nameRaw

        # Back-of-card pages repeat the operative name — skip them
        if ($processed.Contains($operativeName)) {
            $i++
            continue
        }

        # ── Stats values ─────────────────────────────────────────────────────
        $i++

        while ($i -lt $count -and [string]::IsNullOrWhiteSpace($lines[$i])) {
            $i++
        }

        if ($i -ge $count) {
            break
        }

        $statsLine = $lines[$i]

        # Some PDFs split the save stat across two lines (digit on line N, + on line N+1)
        if ($statsLine -match '\d+\s*$' -and ($i + 1) -lt $count -and $lines[$i + 1] -match '^\s*\+') {
            $statsLine = $statsLine + $lines[$i + 1]
            $i++
        }

        $apl = 0; $move = 0; $save = '?'; $wounds = 0

        if ($statsLine -match '(\d+)\s+(\d+)["\s]+(\d+)\s*\+\s+(\d+)') {
            $apl    = [int]$Matches[1]
            $move   = [int]$Matches[2]
            $save   = "$($Matches[3])+"
            $wounds = [int]$Matches[4]
        }

        # ── Locate weapon-table header (NAME ATK HIT DMG) ────────────────────
        $i++
        $foundTable = $false

        while ($i -lt $count) {
            if ($lines[$i] -match '\bNAME\b.*\bATK\b.*\bHIT\b.*\bDMG\b') {
                $foundTable = $true
                break
            }

            # Hit the next card's header before finding a weapon table — back up
            if ($lines[$i] -match '\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b') {
                $i--
                break
            }

            $i++
        }

        if (-not $foundTable) {
            continue
        }

        $i++ # past the NAME/ATK/HIT/DMG header

        # ── Parse weapon rows ─────────────────────────────────────────────────
        $weapons = [System.Collections.Generic.List[hashtable]]::new()

        while ($i -lt $count) {
            $wLine = $lines[$i]

            # Next card starts
            if ($wLine -match '\bAPL\b.*\bMOVE\b.*\bSAVE\b.*\bWOUNDS\b') {
                break
            }

            # Faction keywords line — also extract faction from it
            if ($wLine -match '^[A-Z0-9\s,\-]+$' -and ($wLine.Split(',').Count -ge 3)) {
                if (-not $faction) {
                    $parts = $wLine.Trim() -split '\s*,\s*'

                    if ($parts.Count -ge 3) {
                        $faction = ConvertTo-TitleCase ($parts[2].Trim() -replace '\s*\d+\s*$', '')
                    }
                }

                break
            }

            if ([string]::IsNullOrWhiteSpace($wLine) -or $wLine -match 'RULES CONTINUE|RULE CONTINUES') {
                $i++
                continue
            }

            # Weapon row: name  (2+ spaces)  atk  hit+  dmg/dmg  [rules]
            if ($wLine -match '^\s*(.+?)\s{2,}(\d+)\s+(\d+)\+\s+(\d+)/(\d+)(.*)') {
                $wName  = $Matches[1].Trim()
                $wAtk   = [int]$Matches[2]
                $wHit   = "$($Matches[3])+"
                $wDmg   = "$($Matches[4])/$($Matches[5])"
                $wRules = $Matches[6].Trim()

                # Absorb continuation lines (heavily indented, no stats pattern)
                while (($i + 1) -lt $count) {
                    $next = $lines[$i + 1]

                    if ($next -match '^\s{15,}\S' -and $next -notmatch '\d+\s*\+\s*\d+/\d+') {
                        $wRules = ($wRules + ' ' + $next.Trim()).Trim()
                        $i++
                    }
                    else {
                        break
                    }
                }

                $wRules = $wRules.Trim()

                if ($wRules -eq '-') {
                    $wRules = ''
                }

                $weapons.Add([ordered]@{
                    name         = $wName
                    type         = Get-WeaponType $wName $wRules
                    atk          = $wAtk
                    hit          = $wHit
                    dmg          = $wDmg
                    specialRules = $wRules
                })
            }

            $i++
        }

        if ($weapons.Count -gt 0) {
            [void]$processed.Add($operativeName)

            $operatives.Add([ordered]@{
                name   = $operativeName
                stats  = [ordered]@{
                    move   = $move
                    apl    = $apl
                    wounds = $wounds
                    save   = $save
                }
                weapons = $weapons
            })
        }
    }

    return @{
        operatives = $operatives
        faction    = $faction
    }
}

# ---------------------------------------------------------------------------
# Slug and filename helpers
# ---------------------------------------------------------------------------

function Get-TeamSlug([string]$name) {
    return ($name.ToLower() -replace '[^a-z0-9]+', '_').Trim('_')
}

function Get-TeamFileName([string]$name) {
    return ($name.ToLower() -replace '[^a-z0-9]+', '-').Trim('-') + '.json'
}

# ---------------------------------------------------------------------------
# Main extraction
# ---------------------------------------------------------------------------

function Extract-Team([string]$teamName) {
    $teamFolder = Join-Path $ReferencesBase $teamName

    if (-not (Test-Path $teamFolder)) {
        Write-Warning "Skipping '$teamName' — folder not found at $teamFolder"
        return
    }

    $outFile = Join-Path $TeamsOut (Get-TeamFileName $teamName)

    if ((Test-Path $outFile) -and -not $Force) {
        Write-Host "  Skipping '$teamName' — $outFile already exists (use -Force to overwrite)" -ForegroundColor Yellow
        return
    }

    Write-Host "`n==> Extracting: $teamName" -ForegroundColor Cyan

    $dcPdf = Get-ChildItem $teamFolder -Filter '*Datacards*'        | Select-Object -First 1
    $fePdf = Get-ChildItem $teamFolder -Filter '*Faction Equipment*' | Select-Object -First 1
    $uePdf = Get-ChildItem $teamFolder -Filter '*Universal Equipment*' | Select-Object -First 1

    if (-not $dcPdf) {
        Write-Warning "  No Datacards PDF found — skipping."
        return
    }

    Write-Host "  Parsing datacards..." -ForegroundColor Gray
    $parsed     = Parse-Datacards $dcPdf.FullName
    $operatives = $parsed.operatives
    $faction    = $parsed.faction ?? 'UNKNOWN — UPDATE ME'

    Write-Host "  Operatives : $($operatives.Count)" -ForegroundColor Gray
    Write-Host "  Faction    : $faction" -ForegroundColor Gray

    $equipPaths = @()
    if ($fePdf) { $equipPaths += $fePdf.FullName }
    if ($uePdf) { $equipPaths += $uePdf.FullName }

    Write-Host "  Parsing equipment..." -ForegroundColor Gray
    $equipment = if ($equipPaths) { @(Parse-Equipment $equipPaths) } else { @() }
    Write-Host "  Equipment  : $($equipment.Count) items" -ForegroundColor Gray

    if ($operatives.Count -eq 0) {
        Write-Warning "  No operatives extracted — aborting. The PDF layout may differ from expected."
        return
    }

    $slug = Get-TeamSlug $teamName

    $finalOperatives = @($operatives | ForEach-Object {
        [ordered]@{
            name          = $_.name
            operativeType = $_.name
            stats         = $_.stats
            weapons       = @($_.weapons)
            equipment     = $equipment
        }
    })

    $team = [ordered]@{
        '$schema'  = '../schema/team.schema.json'
        id         = $slug
        name       = $teamName
        faction    = $faction
        operatives = $finalOperatives
    }

    $json = $team | ConvertTo-Json -Depth 10

    # Schema validation
    try {
        if (Test-Json -Json $json -SchemaFile $SchemaPath 2>$null) {
            Write-Host "  ✓ Schema valid" -ForegroundColor Green
        }
        else {
            Write-Warning "  Schema validation failed — review output manually"
        }
    }
    catch {
        Write-Warning "  Schema validation error: $_"
    }

    $json | Set-Content -Path $outFile -Encoding UTF8
    Write-Host "  Written: $outFile" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

Assert-PdfToText

if ($All) {
    Get-ChildItem $ReferencesBase -Directory | ForEach-Object {
        Extract-Team $_.Name
    }
}
else {
    Extract-Team $TeamName
}
