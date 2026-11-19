# SeatWatch 빌드 및 배포. 지정한 파일만 압축한다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ('SeatWatchBuild-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'
$names = 'SeatWatch.exe', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll'
try {
    & (Join-Path $root 'build.bat') $stage
    if ($LASTEXITCODE -ne 0) { throw 'SeatWatch 빌드 실패' }
    $files = @($names | ForEach-Object { Join-Path $stage $_ })
    $files += Join-Path $root 'dist-files\사용법.txt'
    $files += Join-Path $root 'lib\WebView2-LICENSE.txt'
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $zip = Join-Path $dist ('SeatWatch-' + (Get-Item $files[0]).LastWriteTime.ToString('yyyyMMdd') + '.zip')
    $temp = Join-Path $dist ([guid]::NewGuid().ToString('N') + '.tmp')
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    try {
        $archive = [IO.Compression.ZipFile]::Open($temp, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($path in $files) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $path,
                    'SeatWatch/' + (Split-Path $path -Leaf), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }

        $archive = [IO.Compression.ZipFile]::OpenRead($temp)
        try {
            if ($archive.Entries.Count -ne $files.Count) { throw '배포 파일 개수가 일치하지 않습니다.' }
            foreach ($path in $files) {
                $entry = $archive.GetEntry('SeatWatch/' + (Split-Path $path -Leaf))
                $stream = $entry.Open()
                try { $packed = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
                finally { $stream.Dispose() }
                if ($packed -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw "압축 파일 검증 실패: $path" }
            }
        } finally { $archive.Dispose() }
        if ([IO.File]::Exists($zip)) { [IO.File]::Replace($temp, $zip, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temp, $zip) }
        Write-Host "배포 파일 검증 완료: $zip"
    } finally {
        if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
    }

    # 같은 빌드를 로컬 실행 폴더에도 게시한다. 사용 중이면 검증된 ZIP을 사용할 수 있다.
    New-Item -ItemType Directory -Force $app | Out-Null
    foreach ($name in $names) {
        $dest = Join-Path $app $name
        $source = Join-Path $stage $name
        if ([IO.File]::Exists($dest) -and (Get-FileHash $dest).Hash -eq (Get-FileHash $source).Hash) { continue }
        $update = Join-Path $app ([guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::Copy($source, $update)
            if ([IO.File]::Exists($dest)) { [IO.File]::Replace($update, $dest, [System.Management.Automation.Language.NullString]::Value) }
            else { [IO.File]::Move($update, $dest) }
        } catch [IO.IOException] {
            Write-Warning "로컬 실행 파일을 갱신하지 못했습니다. SeatWatch 종료 후 ZIP을 풀어 주세요. $dest"
        } finally { if ([IO.File]::Exists($update)) { [IO.File]::Delete($update) } }
    }
} finally {
    foreach ($name in $names) {
        $file = Join-Path $stage $name
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
    }
    if ([IO.Directory]::Exists($stage)) { [IO.Directory]::Delete($stage) }
}
