@echo off
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [x] csc.exe not found. Please install .NET Framework 4.x.
    exit /b 1
)

if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /out:dist\DesktopIconChanger.exe ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    src\DesktopIconChanger.cs

if errorlevel 1 (
    echo [x] build failed
    exit /b 1
)

echo [ok] built dist\DesktopIconChanger.exe
