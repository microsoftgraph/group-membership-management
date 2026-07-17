$ErrorActionPreference = "Stop"

# =============================================================================
#  DeploymentLogging.ps1 - GMM deployment logging helper (dot-sourceable)
# =============================================================================
#  Provides a single, consistent, machine-parseable logging convention for the
#  GMM deployment scripts. Dot-source this file (function-wrapper pattern: only
#  function definitions execute at load, no side effects) to import:
#
#    Write-DeployLog     - severity-colored, per-line-timestamped narration
#    Write-DeployPhase   - stable phase boundary markers
#    Write-DeployResult  - terminal SUCCESS/FAILED marker
#    Write-DeployError   - structured error marker
#
#  ---------------------------------------------------------------------------
#  ASCII-ONLY CONTRACT (team convention)
#  ---------------------------------------------------------------------------
#  Every character emitted by this file, and every marker token below, MUST be
#  7-bit ASCII (0x00-0x7F). Windows PowerShell 5.1 misreads non-ASCII bytes in
#  BOM-less UTF-8 files, so emojis / box-drawing glyphs are forbidden here.
#  Machine parsing of the markers must never depend on any glyph.
#
#  ---------------------------------------------------------------------------
#  LINE FORMAT
#  ---------------------------------------------------------------------------
#  Narration lines (Write-DeployLog) are:
#      [yyyy-MM-dd HH:mm:ssZ] <TAG> <message>
#  where the timestamp is UTC (trailing Z) and <TAG> is a fixed-width ASCII tag:
#      Info    -> [INFO]
#      Success -> [ OK ]
#      Warn    -> [WARN]
#      Error   -> [FAIL]
#
#  ---------------------------------------------------------------------------
#  STABLE MARKER GRAMMAR (P3)
#  ---------------------------------------------------------------------------
#  Marker lines carry an optional leading "[yyyy-MM-dd HH:mm:ssZ] " UTC timestamp for humans;
#  log tooling should treat that prefix as OPTIONAL and key only on the token.
#
#      ##[phase:begin] <PhaseName>
#      ##[phase:end] <PhaseName>
#      ##[deploy:result] SUCCESS
#      ##[deploy:result] FAILED | reason=<summary> | lastPhase=<PhaseName>
#      ##[deploy:error] <Category> | <Message>
#
#  The LAST ##[deploy:result] line in a transcript is the authoritative result.
# =============================================================================

function ConvertTo-DeployAsciiLine {
    # Sanitizes text used inside a machine marker line so the marker grammar
    # stays strictly ASCII, single-line, and cannot inject extra marker fields.
    # Collapses CR/LF/TAB to single spaces, replaces any character outside
    # printable ASCII (0x20-0x7E) with '?', and replaces the marker field
    # separator '|' with '/' so user-provided values (e.g. an exception message)
    # cannot add spurious " | key=value" segments to ##[deploy:result] /
    # ##[deploy:error] lines. Used ONLY for marker lines, not human narration.
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return '' }

    $singleLine = $Text -replace '[\r\n\t]+', ' '
    $ascii = $singleLine -replace '[^\x20-\x7E]', '?'
    return ($ascii -replace '\|', '/')
}

<#
.SYNOPSIS
Writes a severity-colored, timestamped deployment narration line.

.DESCRIPTION
Emits "[yyyy-MM-dd HH:mm:ssZ] <TAG> <message>" (UTC) using a fixed sink per severity:
  Info    -> Write-Host (Gray)
  Success -> Write-Host (Green)
  Warn    -> Write-Warning (Warning stream; captured by Start-Transcript)
  Error   -> Write-Error -ErrorAction Continue (Error stream; NON-terminating)
Error is forced non-terminating because Deploy-Resources.ps1 sets
$ErrorActionPreference = 'Stop'; a bare Write-Error would abort the run. Fatal
errors remain the caller's explicit `throw`.

.PARAMETER Level
Severity: Info (default), Success, Warn, or Error.

.PARAMETER Message
The human-readable message. Mandatory.

.EXAMPLE
Write-DeployLog -Level Success -Message "Resources deployed"
#>
function Write-DeployLog {
    [CmdletBinding()]
    param(
        [ValidateSet('Info', 'Success', 'Warn', 'Error')]
        [string]$Level = 'Info',
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') + 'Z'
    $tag = switch ($Level) {
        'Success' { '[ OK ]' }
        'Warn'    { '[WARN]' }
        'Error'   { '[FAIL]' }
        default   { '[INFO]' }
    }

    # Split embedded newlines so EVERY physical output line carries the
    # timestamp + severity prefix (Write-Host/Warning/Error would otherwise emit
    # continuation lines with no prefix). Drop trailing blank lines that come
    # from callers passing a trailing newline, so no unprefixed dangling line is
    # emitted.
    $messageLines = ($Message -replace "`r`n", "`n" -replace "`r", "`n") -split "`n"
    while ($messageLines.Count -gt 1 -and $messageLines[-1] -eq '') {
        $messageLines = $messageLines[0..($messageLines.Count - 2)]
    }

    foreach ($messageLine in $messageLines) {
        $text = "[{0}] {1} {2}" -f $timestamp, $tag, $messageLine
        switch ($Level) {
            'Success' { Write-Host $text -ForegroundColor Green }
            'Warn'    { Write-Warning $text }
            'Error'   { Write-Error $text -ErrorAction Continue }
            default   { Write-Host $text -ForegroundColor Gray }
        }
    }

    # This line carries visible content, so a following phase-begin should get
    # its leading blank separator.
    $global:GmmLastLineBlank = $false
}

<#
.SYNOPSIS
Emits a stable phase boundary marker.

.DESCRIPTION
Writes "[yyyy-MM-dd HH:mm:ssZ] ##[phase:begin] <Name>" or "... ##[phase:end] <Name>" in Cyan
so phase boundaries stand out from the Gray narration. A blank line is emitted
before a Begin and after an End for visual separation; consecutive blank lines
are collapsed (via $global:GmmLastLineBlank) so adjacent phases are separated by
a single blank line. On a Begin event the phase name is recorded in
$global:GmmCurrentDeployPhase so failure paths can report it as the LastPhase in
a FAILED result marker.

.PARAMETER Name
The phase name (a stable, human-readable label for the deployment phase). Mandatory.

.PARAMETER Event
Begin (default) or End.

.EXAMPLE
Write-DeployPhase -Name 'Data Resources' -Event Begin
#>
function Write-DeployPhase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [ValidateSet('Begin', 'End')]
        [string]$Event = 'Begin'
    )

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') + 'Z'
    # Sanitize the name so a caller-supplied value cannot break the ASCII,
    # single-line marker contract (or inject a marker-looking line).
    $safeName = ConvertTo-DeployAsciiLine $Name

    if ($Event -eq 'End') {
        $token = 'phase:end'
    }
    else {
        $token = 'phase:begin'
        # Track the phase in progress for failure-path LastPhase reporting.
        $global:GmmCurrentDeployPhase = $safeName
        # Blank line before a phase begins, unless the previous line was already
        # blank (e.g. the trailing blank from the prior phase's end) so adjacent
        # phases are separated by a single blank line.
        if (-not $global:GmmLastLineBlank) { Write-Host "" }
    }

    Write-Host ("[{0}] ##[{1}] {2}" -f $timestamp, $token, $safeName) -ForegroundColor Cyan

    if ($Event -eq 'End') {
        # Blank line after a phase ends.
        Write-Host ""
        $global:GmmLastLineBlank = $true
    }
    else {
        $global:GmmLastLineBlank = $false
    }
}

<#
.SYNOPSIS
Emits the terminal deployment result marker.

.DESCRIPTION
Writes "[yyyy-MM-dd HH:mm:ssZ] ##[deploy:result] SUCCESS" or
"[yyyy-MM-dd HH:mm:ssZ] ##[deploy:result] FAILED | reason=<Reason> | lastPhase=<LastPhase>".
Optional segments are omitted cleanly when not supplied. The LAST
##[deploy:result] line in a transcript is the authoritative result.

.PARAMETER Status
SUCCESS or FAILED.

.PARAMETER Reason
Optional failure summary (FAILED only).

.PARAMETER LastPhase
Optional last phase reached (FAILED only).

.EXAMPLE
Write-DeployResult -Status FAILED -Reason "ARM state: Failed" -LastPhase "Compute Resources"
#>
function Write-DeployResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('SUCCESS', 'FAILED')]
        [string]$Status,
        [string]$Reason,
        [string]$LastPhase
    )

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') + 'Z'
    $line = "[{0}] ##[deploy:result] {1}" -f $timestamp, $Status

    if ($Status -eq 'FAILED') {
        if (-not [string]::IsNullOrWhiteSpace($Reason))    { $line += " | reason=$(ConvertTo-DeployAsciiLine $Reason)" }
        if (-not [string]::IsNullOrWhiteSpace($LastPhase)) { $line += " | lastPhase=$(ConvertTo-DeployAsciiLine $LastPhase)" }
    }

    $color = if ($Status -eq 'SUCCESS') { 'Green' } else { 'Red' }
    Write-Host $line -ForegroundColor $color
    $global:GmmLastLineBlank = $false
}

<#
.SYNOPSIS
Emits a structured, machine-parseable error marker.

.DESCRIPTION
Writes "[yyyy-MM-dd HH:mm:ssZ] ##[deploy:error] <Category> | <Message>" to the host so the
line is captured by Start-Transcript. This marker is NON-terminating by design;
fatal handling stays with the caller's explicit `throw`.

.PARAMETER Category
Short error category (e.g. 'ARM Deployment', 'ARM Operation'). Mandatory.

.PARAMETER Message
Error detail. Mandatory.

.EXAMPLE
Write-DeployError -Category 'ARM Deployment' -Message 'Deployment failed. Final state: Failed'
#>
function Write-DeployError {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Category,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') + 'Z'
    Write-Host ("[{0}] ##[deploy:error] {1} | {2}" -f $timestamp, (ConvertTo-DeployAsciiLine $Category), (ConvertTo-DeployAsciiLine $Message)) -ForegroundColor Red
    $global:GmmLastLineBlank = $false
}
