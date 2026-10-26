@echo off
setlocal
where msbuild.exe >nul 2>nul
if errorlevel 1 (
  echo MSBuild was not found.
  echo Install Visual Studio Build Tools with the .NET desktop build tools workload.
  echo Then run this file from a Developer Command Prompt for Visual Studio.
  pause
  exit /b 1
)
msbuild "%~dp0SayMate.csproj" /restore /t:Build /p:Configuration=Release
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo Build complete: %~dp0bin\Release\SayMate.exe
pause
