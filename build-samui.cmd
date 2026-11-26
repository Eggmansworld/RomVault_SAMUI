@echo off
REM Builds SAM-UI-Avalonia (Release) and writes the full output to build.log
cd /d "%~dp0"
echo Building SAM-UI-Avalonia... (log: %~dp0build.log)
dotnet --version > build.log 2>&1
dotnet build "SAM-UI-Avalonia\SAM-UI-Avalonia.csproj" -c Release -nologo "-clp:ErrorsOnly;Summary" >> build.log 2>&1
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see build.log
  type build.log
  pause
  exit /b 1
)
echo.
echo BUILD OK
echo Exe: %~dp0SAM-UI-Avalonia\bin\Release\net10.0\SAM-UI-Avalonia.exe
start "" "%~dp0SAM-UI-Avalonia\bin\Release\net10.0\SAM-UI-Avalonia.exe"
pause
