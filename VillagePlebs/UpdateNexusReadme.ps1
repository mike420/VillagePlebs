param ($readmeFile, $nexusFile)

# Converts the ThunderStore README (markdown) into BBCode for the Nexus mod page.
# Nexus has no tables or inline code, and shows emoji as "?", so the config table becomes one list per section,
# inline code becomes bold, and emoji are dropped.

function Convert-Inline([string] $text) {
    $text = [regex]::Replace($text, '<a href="([^"]+)">!\[[^\]]*\]\(([^)]+)\)</a>', '[url=$1][img]$2[/img][/url]')
    $text = [regex]::Replace($text, '!\[[^\]]*\]\(([^)]+)\)', '[img]$1[/img]')
    $text = [regex]::Replace($text, '<a href="([^"]+)">(.*?)</a>', '[url=$1]$2[/url]')
    $text = [regex]::Replace($text, '`([^`]+)`', '[b]$1[/b]')
    $text = [regex]::Replace($text, '\*\*(.+?)\*\*', '[b]$1[/b]')
    $text = [regex]::Replace($text, '<(strong|b)>(.*?)</\1>', '[b]$2[/b]')
    return $text
}

# Inline <br> becomes a line break, except one at the end of the line, which the line's own break replaces;
# done after Remove-Emoji so its Trim keeps trailing breaks
function Convert-Line([string] $text) {
    $text = (Remove-Emoji (Convert-Inline $text)) -replace '\s*<br\s*/?>$', ''
    return ($text -replace '\s*<br\s*/?>\s*', "`r`n")
}

function Remove-Emoji([string] $text) {
    # Surrogate pairs (most emoji), the symbol blocks with the other emoji used, and the emoji variation selector
    $emoji = '[' + [char]0xD800 + '-' + [char]0xDFFF + [char]0x2600 + '-' + [char]0x27BF + [char]0x2B00 + '-' + [char]0x2BFF + [char]0xFE0F + ']'
    $text = [regex]::Replace($text, $emoji, '')
    return ([regex]::Replace($text, ' {2,}', ' ')).Trim()
}

function Convert-Heading([string] $text, [int] $size) {
    $text = Remove-Emoji $text
    $rest = $null
    $link = $text.IndexOf('<a ')
    if ($link -ge 0) {
        $rest = $text.Substring($link).Trim().TrimEnd('.')
        $text = $text.Substring(0, $link)
    }
    $heading = $text.Trim().TrimEnd(':').Trim()
    $lines = @("[size=$size][b]$heading[/b][/size]")
    if ($rest) {
        $lines += ''
        $lines += (Convert-Inline $rest)
    }
    return $lines
}

try {
    $source = [System.IO.File]::ReadAllLines($readmeFile, [System.Text.Encoding]::UTF8)
    $out = New-Object System.Collections.Generic.List[string]
    $inList = $false
    $tableRows = New-Object System.Collections.Generic.List[object]

    function Close-List {
        if ($script:inList) {
            $script:out.Add('[/list]')
            $script:inList = $false
        }
    }

    # Image cells are joined to the text cell after them ("[img]..[/img] Raw Meat"), giving one label per item
    function Join-Cells([string[]] $cells) {
        $parts = New-Object System.Collections.Generic.List[string]
        $image = ''
        foreach ($cell in $cells) {
            $cell = Convert-Inline $cell
            if ($cell -match '^\[img\].*\[/img\]$') {
                $image = $cell
                continue
            }
            if ($cell -eq '') { continue }
            $parts.Add((($image + ' ' + $cell).Trim()))
            $image = ''
        }
        if ($image) { $parts.Add($image) }
        return $parts
    }

    # The config table (icon | Piece | Setting | Default, or Section | Setting | Default | What it does; Piece or Section blank on follow-up rows) becomes one list per piece;
    # any other table becomes one list item per row
    function Flush-Table {
        $rows = $script:tableRows
        if ($rows.Count -eq 0) { return }
        $script:tableRows = New-Object System.Collections.Generic.List[object]
        Close-List

        $header = $rows[0]
        $body = @($rows | Select-Object -Skip 1 | Where-Object { -not (($_ -join '') -match '^[\s:\-]*$') })

        if (($header -contains 'Setting') -and ($header -contains 'Default')) {
            $pieceColumn = [array]::IndexOf($header, 'Piece')
            if ($pieceColumn -lt 0) { $pieceColumn = [array]::IndexOf($header, 'Section') }
            $settingColumn = [array]::IndexOf($header, 'Setting')
            $defaultColumn = [array]::IndexOf($header, 'Default')
            $started = $false
            foreach ($cells in $body) {
                $piece = $cells[$pieceColumn]
                if ($piece -or -not $started) {
                    if (-not $piece) { $piece = 'General' }
                    Close-List
                    if ($started) { $script:out.Add('') }
                    $label = (@(Join-Cells $cells[0..$pieceColumn]) -join ' ')
                    if (-not $label) { $label = $piece }
                    $label = $label -replace [regex]::Escape($piece), "[b]$piece[/b]"
                    $script:out.Add($label)
                    $script:out.Add('[list]')
                    $script:inList = $true
                    $started = $true
                }
                $script:out.Add("[*][b]$($cells[$settingColumn])[/b] (default $(Convert-Inline $cells[$defaultColumn]))")
            }
        }
        else {
            $script:out.Add('[list]')
            $script:inList = $true
            foreach ($cells in $body) {
                $parts = @(Join-Cells $cells)
                if ($parts.Count -eq 0) { continue }
                $first = $parts[0]
                if ($first -match '^(\[img\][^\[]*\[/img\] )(.+)$') { $first = "$($Matches[1])[b]$($Matches[2])[/b]" }
                elseif ($first -notmatch '^\[img\]') { $first = "[b]$first[/b]" }
                $script:out.Add('[*]' + ((@($first) + @($parts | Select-Object -Skip 1)) -join ' - '))
            }
        }
        Close-List
    }

    foreach ($raw in $source) {
        $line = $raw.TrimEnd()
        $trimmed = $line.Trim()

        if ($trimmed -match '^\|') {
            # Strip exactly one outer pipe each side so a leading empty cell ("||") is kept
            $inner = $trimmed -replace '^\|', '' -replace '\|$', ''
            $tableRows.Add([string[]]($inner.Split('|') | ForEach-Object { $_.Trim() }))
            continue
        }
        Flush-Table

        if ($trimmed -match '^\* (.*)$') {
            if (-not $inList) {
                $out.Add('[list]')
                $inList = $true
            }
            $out.Add('[*]' + (Convert-Line $Matches[1]))
            continue
        }
        Close-List

        # <details>/<summary> collapsibles become a bold title followed by a Nexus spoiler
        if ($trimmed -match '^<details>$') {
            continue
        }
        if ($trimmed -match '^<summary>(.*)</summary>$') {
            $summary = $Matches[1] -replace '</?(strong|b)>', ''
            $out.Add('[b]' + (Remove-Emoji (Convert-Inline $summary)) + '[/b]')
            $out.Add('[spoiler]')
            continue
        }
        if ($trimmed -match '^</details>$') {
            $out.Add('[/spoiler]')
            continue
        }

        if ($trimmed -match '^(<br\s*/?>)+$' -or $trimmed -eq '') {
            $out.Add('')
        }
        elseif ($trimmed -match '^### (.*)$') {
            $out.Add('[b]' + (Remove-Emoji $Matches[1]) + '[/b]')
        }
        elseif ($trimmed -match '^## (.*)$') {
            $out.AddRange([string[]](Convert-Heading $Matches[1] 5))
        }
        elseif ($trimmed -match '^# (.*)$') {
            $out.AddRange([string[]](Convert-Heading $Matches[1] 6))
        }
        else {
            $out.Add((Convert-Line $trimmed))
        }
    }
    Flush-Table
    Close-List

    # At most two blank lines in a row, a blank line after each heading, none at the ends
    $text = ($out -join "`r`n")
    $text = [regex]::Replace($text, '(\r\n){4,}', "`r`n`r`n`r`n")
    $text = [regex]::Replace($text, '(\[/size\])\r\n(?!\r\n)', "`$1`r`n`r`n")
    $text = $text.Trim() + "`r`n"

    $directory = [System.IO.Path]::GetDirectoryName($nexusFile)
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
    [System.IO.File]::WriteAllText($nexusFile, $text, (New-Object System.Text.UTF8Encoding($false)))
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
