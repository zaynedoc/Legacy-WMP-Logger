[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\publish\desktop'),
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Sign,
    [string]$CodeSigningAccountName = 'wmplwrapdesktop',
    [string]$CertificateProfileName = 'wmplwrapdesktop-public',
    [string]$Endpoint = 'https://eus.codesigning.azure.net'
)

function Find-FirstExistingFile {
    param(
        [Parameter(Mandatory)]
        [string[]]$CandidatePaths
    )

    foreach ($candidatePath in $CandidatePaths) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return (Get-Item -LiteralPath $candidatePath).FullName
        }
    }

    return $null
}

function Get-SignToolPath {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter 'signtool.exe' -File |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$project = Join-Path $PSScriptRoot '..\src\WmplWrap.Desktop\WmplWrap.Desktop.csproj'
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$executablePath = Join-Path $outputPath 'WmplWrap.Desktop.exe'

& dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw 'Desktop publishing failed'
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Desktop publishing succeeded but the expected executable was not found: $executablePath"
}

if ($Sign) {
    $signToolPath = Get-SignToolPath
    $dlibPath = Find-FirstExistingFile -CandidatePaths @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\ArtifactSigningClientTools\bin\Azure.CodeSigning.Dlib.dll'),
        (Join-Path $env:ProgramFiles 'Microsoft\ArtifactSigningClientTools\bin\Azure.CodeSigning.Dlib.dll'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll')
    )

    if (-not $signToolPath -or -not $dlibPath) {
        throw @"
Azure Artifact Signing Client Tools were not found. Install them once with:
  winget install -e --id Microsoft.Azure.ArtifactSigningClientTools

The installer supplies the compatible SignTool and Azure.CodeSigning.Dlib.dll required for -Sign.
"@
    }

    $metadataPath = Join-Path ([System.IO.Path]::GetTempPath()) "wmplwrap-artifact-signing-$([guid]::NewGuid()).json"
    $metadata = [ordered]@{
        Endpoint = $Endpoint
        CodeSigningAccountName = $CodeSigningAccountName
        CertificateProfileName = $CertificateProfileName
    } | ConvertTo-Json

    try {
        # Azure.CodeSigning.Dlib reads its metadata as raw JSON and rejects the UTF-8 BOM
        # emitted by Windows PowerShell's Set-Content -Encoding UTF8.
        [System.IO.File]::WriteAllText($metadataPath, $metadata, [System.Text.UTF8Encoding]::new($false))

        Write-Host "Signing $executablePath with Azure Artifact Signing..."
        Write-Host 'Your browser may open if Azure authentication is required.'
        & $signToolPath sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 `
            /dlib $dlibPath /dmdf $metadataPath $executablePath

        if ($LASTEXITCODE -ne 0) {
            throw 'Desktop signing failed'
        }

        & $signToolPath verify /pa /v $executablePath
        if ($LASTEXITCODE -ne 0) {
            throw 'Desktop signature verification failed'
        }
    }
    finally {
        if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
            Remove-Item -LiteralPath $metadataPath -Force
        }
    }

    Write-Host "Desktop app signed and verified: $executablePath"
}

Write-Host "Desktop app published to $outputPath"
Write-Host "Run WmplWrap.Desktop.exe from that folder or create a desktop shortcut to it"
