@echo off
setlocal enabledelayedexpansion

set AVERAGEBITRATE=3000
set MAXBITRATE=4000

set FF=tools\ffmpeg.exe
set FFFLAGS=-hide_banner -loglevel error -y
set AC=tools\AutoUscV2.exe
set MP=tools\mps2pmf.exe
set GETDURATION=tools\get_duration.exe

if not "%~1"=="" set AVERAGEBITRATE=%~1
if not "%~2"=="" set MAXBITRATE=%~2

if not exist %FF% (
    echo ERROR: ffmpeg not found at %FF%
    pause
    exit /b
)

if not exist %AC% (
    echo ERROR: AutoUsc not found at %AC%
    pause
    exit /b
)

if not exist %MP% (
    echo ERROR: mps2pmf not found at %MP%
    pause
    exit /b
)

if not exist %GETDURATION% (
    echo ERROR: get_duration.exe not found at %GETDURATION%
    pause
    exit /b
)

if not exist "input\mov_edited\" (
    echo ERROR: Folder input\mov_edited\ does not exist.
    pause
    exit /b
)

set "found_mov=0"
for %%f in (input\mov_edited\*.mov) do (
    set "found_mov=1"
    call :mov2avi %%f
)

if "%found_mov%"=="0" (
    echo No .mov files found in input\mov_edited\
)

set "found_avi=0"
if exist obj\ (
    for %%f in (obj\*.avi) do (
        set "found_avi=1"
        call :avi2pmf %%f
    )
)

rd /s /q obj >nul 2>&1
pause
exit /b

:mov2avi
echo.
echo --- Converting %~nx1 to AVI and extracting audio ---
md obj 2>nul
%FF% %FFFLAGS% -i %1 -c:v ffv1 -pix_fmt rgb24 -an obj\%~n1.avi
%FF% %FFFLAGS% -i %1 -vn -ar 44100 obj\%~n1.wav
exit /b

:avi2pmf
echo.
echo --- Processing %~nx1 ---
md output\pmf 2>nul
call %AC% --cn %~n1 --pn %~n1 -a obj\%~n1.wav -v %1 -o obj\%~n1.MPS --averagebitrate %AVERAGEBITRATE% --maxbitrate %MAXBITRATE%

for /f "tokens=1,2 delims=," %%a in ('%GETDURATION% "obj\%~n1.MPS"') do (
    set min=%%a
    set sec=%%b
)

echo Duration: %min% minutes and %sec% seconds

%MP% -i obj\%~n1.MPS -o output\pmf\%~n1.pmf -m %min% -s %sec%
exit /b
