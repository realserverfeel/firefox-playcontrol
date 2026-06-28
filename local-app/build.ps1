# Builds a single self-contained win-x64 executable for the PlayControl Agent.
# Output: local-app\publish\PlayControlAgent.exe (no .NET runtime needed to run).
#
# Usage (from this folder, on Windows with the .NET 8 SDK installed):
#   .\build.ps1
$ErrorActionPreference = "Stop"
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
Write-Host "Built: $(Resolve-Path .\publish\PlayControlAgent.exe)"
