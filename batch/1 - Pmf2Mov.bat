@echo off
setlocal
set FF=tools\ffmpeg.exe
set FFFLAGS=-hide_banner -loglevel error -y
set PD=tools\psmfdump.exe

if not exist "%FF%" (
    echo ERROR: ffmpeg not found at %FF%
    pause
    exit /b
)

if not exist "%PD%" (
    echo ERROR: psmfdump not found at %PD%
    pause
    exit /b
)

if not exist "input\pmf\" (
    echo ERROR: Folder input\pmf\ does not exist.
    pause
    exit /b
)

set "found=0"
for %%f in (input\pmf\*.pmf) do (
    set "found=1"
    call :pmf2mov %%f
)

if "%found%"=="0" (
    echo No .pmf files found in input\pmf\
)

rd /s /q obj 2>nul
pause
exit /b

:pmf2mov
echo.
echo --- Processing file %1 ---
if exist obj\ (
  rd /s /q obj
)
md "obj" 2>nul
md "output\mov" 2>nul

%PD% %1 -a obj\%~n1.oma -v obj\%~n1.264

if exist obj\%~n1.oma if exist obj\%~n1.264 (
    %FF% %FFFLAGS% -i obj\%~n1.264 -i obj\%~n1.oma -map 0 -map 1 -s 480x272 -c:v prores_ks -profile:v 3 output\mov\%~n1.mov
) else (
    echo ERROR: Failed to extract video/audio from %1
)
exit /b
