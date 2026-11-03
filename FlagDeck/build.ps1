# Local build: produces dist\FlagDeck-Setup.exe  (needs .NET 8 SDK + Inno Setup 6)
$ErrorActionPreference = "Stop"
$ver = "1.0.0"
dotnet publish src\FlagDeck.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:Version=$ver -o publish
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
& $iscc "/DAppVersion=$ver" installer\FlagDeck.iss
(Get-FileHash dist\FlagDeck-Setup.exe -Algorithm SHA256).Hash.ToLower() | Set-Content dist\FlagDeck-Setup.exe.sha256 -NoNewline
Write-Host "Done: dist\FlagDeck-Setup.exe"
