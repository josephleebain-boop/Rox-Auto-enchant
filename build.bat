@echo off
REM Builds a self-contained single-file x64 exe into .\dist\RoxStatReroller.exe
REM Needs the .NET 8 SDK once (build machine only):  winget install Microsoft.DotNet.SDK.8
where dotnet >nul 2>nul || (echo .NET SDK not found. Run:  winget install Microsoft.DotNet.SDK.8 & exit /b 1)
dotnet publish src\RoxStatReroller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo.
echo Done: dist\RoxStatReroller.exe
