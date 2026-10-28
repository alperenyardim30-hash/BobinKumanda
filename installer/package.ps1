# Yayın paketlerini artifacts/release altında üretir: her mimari için taşınabilir zip ve kurulum dosyası.
# İkisi de .NET çalışma ortamını içinde taşır; hedef bilgisayarda internet ya da .NET gerekmez.
# Kullanım: ./installer/package.ps1 [-Version 0.1.0] [-Arch x64,x86]   (Inno Setup 6 gerekir)
param(
    [string]$Version,
    [string[]]$Arch = @('x64', 'x86')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    $Version = (Select-Xml -Path "$root/Directory.Build.props" -XPath '//Version').Node.InnerText
}

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6 bulunamadı: https://jrsoftware.org/isdl.php'
}

$out = Join-Path $root 'artifacts/release'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($a in $Arch) {
    $publish = @('publish', "$root/src/BobinKumanda.App", '-c', 'Release', '-r', "win-$a", '--self-contained',
        "-p:Version=$Version", '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')

    # Taşınabilir: sıkıştırılmış tek dosya, indirmesi küçük
    $portable = Join-Path $root "artifacts/portable-$a"
    dotnet @publish '-p:EnableCompressionInSingleFile=true' -o $portable
    if ($LASTEXITCODE) { throw "dotnet publish ($a, taşınabilir) başarısız" }
    Copy-Item "$root/LICENSE" "$portable/LICENSE.txt"
    Compress-Archive -Force -Path "$portable/BobinKumanda.exe", "$portable/LICENSE.txt" `
        -DestinationPath "$out/BobinKumanda-$Version-portable-$a.zip"

    # Kurulum: sıkıştırmasız tek dosya, zayıf bilgisayarda her açılışta açılmayı beklemez; boyutu Inno Setup küçültür
    $setup = Join-Path $root "artifacts/setup-$a"
    dotnet @publish '-p:EnableCompressionInSingleFile=false' -o $setup
    if ($LASTEXITCODE) { throw "dotnet publish ($a, kurulum) başarısız" }
    & $iscc /Q "/DAppVersion=$Version" "/DArch=$a" "/DSourceDir=$setup" "/O$out" "$root/installer/BobinKumanda.iss"
    if ($LASTEXITCODE) { throw "Inno Setup ($a) başarısız" }
}

Get-ChildItem $out -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content -Encoding ascii "$out/SHA256SUMS.txt"
Get-ChildItem $out -File | ForEach-Object { '{0,-44} {1,6:N1} MB' -f $_.Name, ($_.Length / 1MB) }
