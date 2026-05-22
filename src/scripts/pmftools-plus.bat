@echo off
setlocal enabledelayedexpansion
pushd "%~dp0" >nul

set CFGFILE=pmftools-plus.cfg
set WORKERFILE=worker.ini

set FF=tools\ffmpeg.exe
set FFFLAGS=-hide_banner -loglevel error -y
set PD=tools\psmfdump.exe
set OMPS=tools\oMPSComposer.exe
set MP=tools\mps2pmf.exe
set GETDURATION=tools\get_duration.exe
set AT3=tools\at3tool.exe

call :check_tools
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
call :load_cfg
call :normalize_audio_settings
if not exist "%CFGFILE%" call :save_cfg
call :ensure_workspace

:menu
cls
echo.
echo ">>=================================================================<<";
echo "|| ____  __  __ _____ _              _                 _           ||";
echo "||| _ \|  \/  |  ___| |_ ___   ___ | |___       _ __  | |_   _ ___ ||";
echo "||| |_) | |\/| | |_  | __/ _ \ / _ \| / __|_____| '_ \| | | | / __|||";
echo "|||  __/| |  | |  _| | || (_) | (_) | \__ \_____| |_) | | |_| \__ \||";
echo "|||_|   |_|  |_|_|    \__\___/ \___/|_|___/     | .__/|_|\__,_|___/||";
echo "||                                              |_|                ||";
echo ">>=================================================================<<";
echo       by Alex "OAleex" Felix ^@2026
echo       Your all-in-one PSP PMF toolkit.
echo.
echo    A.  PMF  --^>  VIDEO
echo    B.  VIDEO --^>  PMF
echo.
echo    S.  Encoding Settings
echo.
echo    C.  Credits
echo.
echo    X.  Exit
echo.
echo  ============================================================
echo.
set /p CHOICE=   Choose:

if /i "%CHOICE%"=="A" goto pmf2mov
if /i "%CHOICE%"=="B" goto video2pmf
if /i "%CHOICE%"=="S" goto encoding_settings
if /i "%CHOICE%"=="C" goto credits
if /i "%CHOICE%"=="X" exit /b
goto menu

:credits
cls
echo.
echo "+----------------------------------------------------+";
echo "|   ______                     __    _    __         |";
echo "|  / ____/   _____  ___   ____/ /   (_)  / /_   _____|";
echo "| / /       / ___/ / _ \ / __  /   / /  / __/  / ___/|";
echo "|/ /___    / /    /  __// /_/ /   / /  / /_   (__  ) |";
echo "|\____/   /_/     \___/ \__,_/   /_/   \__/  /____/  |";
echo "+----------------------------------------------------+";
echo.
echo   PMFtools-plus
echo     Created by Alex "OAleex" Felix
echo.
echo   Original project lineage
echo     Based on TeamPBCN/pmftools
echo     by LITTOMA
echo.
echo   psmfdump
echo     Included from the TeamPBCN/pmftools lineage
echo     by LITTOMA
echo     Based on VGMToolbox
echo.
echo   Mps2Pmf
echo     Included from the TeamPBCN/pmftools lineage
echo     by LITTOMA
echo     Based on piccahoe's PMF Creater
echo.
echo   oMPSComposer
echo     Created by Alex "OAleex" Felix
echo.
echo   get_duration
echo     Created by Alex "OAleex" Felix
echo.
echo   FFmpeg
echo     Thanks to the FFmpeg project
echo     for essential video encoding support
echo.
echo  ============================================================
echo.
echo   V.  Back
echo.
echo  ============================================================
echo.
set /p CCHOICE=   Choose:

if /i "%CCHOICE%"=="V" goto menu
goto credits

:encoding_settings
call :audio_label
call :source_bitrate_label
cls
echo.
echo  ============================================================
echo   Encoding Settings
echo  ============================================================
echo.
echo   Current values:
echo     Average Bitrate : %AVERAGEBITRATE%
echo     Max Bitrate     : %MAXBITRATE%
echo     Audio Channels  : %AUDIOCHANNELMODE% (%AUDIOCHANNELS%ch)
echo     ATRAC Bitrate   : %ATRACBITRATE% Kbps
echo     Source Match    : %SOURCEBITRATEMODE%
echo.
echo   Advanced oMPSComposer settings:
echo     Encode Mode     : %ENCODEMODE%
echo     IDR Interval    : %IDRDURATION% ms
echo     M-Frames        : %MFRAMES%
echo.
echo   A.  Set Average Bitrate
echo   B.  Set Max Bitrate
echo   C.  Set Audio Channels
echo   D.  Set ATRAC Bitrate
echo.
echo   E.  Set Encode Mode (1pass/2pass)
echo   F.  Set IDR Interval
echo   G.  Set M-Frames
echo   Y.  Toggle Source Bitrate Match
echo.
echo   R.  Reset to defaults
echo   V.  Back
echo.
echo  ============================================================
echo.
set /p BCHOICE=   Choose:

if /i "%BCHOICE%"=="A" goto set_avg
if /i "%BCHOICE%"=="B" goto set_max
if /i "%BCHOICE%"=="C" goto set_audio_channels
if /i "%BCHOICE%"=="D" goto set_atrac
if /i "%BCHOICE%"=="E" goto set_mode
if /i "%BCHOICE%"=="F" goto set_idr
if /i "%BCHOICE%"=="G" goto set_mframes
if /i "%BCHOICE%"=="Y" goto toggle_source_bitrate
if /i "%BCHOICE%"=="R" goto reset_defaults
if /i "%BCHOICE%"=="V" goto menu
goto encoding_settings

:set_mode
echo.
echo   1. 1pass (Faster)
echo   2. 2pass (Better quality)
set /p MCHOICE=   Choose (current: %ENCODEMODE%): 
if "%MCHOICE%"=="1" set ENCODEMODE=1pass
if "%MCHOICE%"=="2" set ENCODEMODE=2pass
call :save_cfg
goto encoding_settings

:set_idr
echo.
set /p NEW_IDR=   New IDR Interval in ms (default: 2000): 
if "%NEW_IDR%"=="" goto encoding_settings
echo %NEW_IDR%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 ( echo   ERROR: Enter a valid number. & pause & goto encoding_settings )
set IDRDURATION=%NEW_IDR%
call :save_cfg
goto encoding_settings

:set_mframes
echo.
set /p NEW_MF=   New M-Frames value (default: 1): 
if "%NEW_MF%"=="" goto encoding_settings
echo %NEW_MF%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 ( echo   ERROR: Enter a valid number. & pause & goto encoding_settings )
set MFRAMES=%NEW_MF%
call :save_cfg
goto encoding_settings

:set_avg
echo.
set /p NEW_AVG=   New Average Bitrate (current: %AVERAGEBITRATE%): 
if "%NEW_AVG%"=="" goto encoding_settings
echo %NEW_AVG%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 ( echo   ERROR: Enter a valid number. & pause & goto encoding_settings )
set AVERAGEBITRATE=%NEW_AVG%
call :save_cfg
echo   [OK] Average Bitrate set to %AVERAGEBITRATE%
pause
goto encoding_settings

:set_max
echo.
set /p NEW_MAX=   New Max Bitrate (current: %MAXBITRATE%): 
if "%NEW_MAX%"=="" goto encoding_settings
echo %NEW_MAX%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 ( echo   ERROR: Enter a valid number. & pause & goto encoding_settings )
set MAXBITRATE=%NEW_MAX%
call :save_cfg
echo   [OK] Max Bitrate set to %MAXBITRATE%
pause
goto encoding_settings

:set_atrac
echo.
call :audio_label
set "ATRAC_SELECTED="
echo   ATRAC3plus bitrate for %AUDIOCHANNELMODE% audio
echo.
if "%AUDIOCHANNELS%"=="1" goto set_atrac_mono
goto set_atrac_stereo

:set_atrac_mono
echo   1.  32 Kbps
echo   2.  48 Kbps
echo   3.  64 Kbps
echo   4.  96 Kbps
echo   5. 128 Kbps
echo.
set /p ACHOICE=   Choose (current: %ATRACBITRATE% Kbps): 
if "%ACHOICE%"=="" goto encoding_settings
if "%ACHOICE%"=="1" set ATRACBITRATE=32
if "%ACHOICE%"=="1" set ATRAC_SELECTED=1
if "%ACHOICE%"=="2" set ATRACBITRATE=48
if "%ACHOICE%"=="2" set ATRAC_SELECTED=1
if "%ACHOICE%"=="3" set ATRACBITRATE=64
if "%ACHOICE%"=="3" set ATRAC_SELECTED=1
if "%ACHOICE%"=="4" set ATRACBITRATE=96
if "%ACHOICE%"=="4" set ATRAC_SELECTED=1
if "%ACHOICE%"=="5" set ATRACBITRATE=128
if "%ACHOICE%"=="5" set ATRAC_SELECTED=1
goto set_atrac_finish

:set_atrac_stereo
echo   1.  48 Kbps
echo   2.  64 Kbps
echo   3.  96 Kbps
echo   4. 128 Kbps
echo   5. 160 Kbps
echo   6. 192 Kbps
echo   7. 256 Kbps
echo   8. 320 Kbps
echo   9. 352 Kbps
echo.
set /p ACHOICE=   Choose (current: %ATRACBITRATE% Kbps): 
if "%ACHOICE%"=="" goto encoding_settings
if "%ACHOICE%"=="1" set ATRACBITRATE=48
if "%ACHOICE%"=="1" set ATRAC_SELECTED=1
if "%ACHOICE%"=="2" set ATRACBITRATE=64
if "%ACHOICE%"=="2" set ATRAC_SELECTED=1
if "%ACHOICE%"=="3" set ATRACBITRATE=96
if "%ACHOICE%"=="3" set ATRAC_SELECTED=1
if "%ACHOICE%"=="4" set ATRACBITRATE=128
if "%ACHOICE%"=="4" set ATRAC_SELECTED=1
if "%ACHOICE%"=="5" set ATRACBITRATE=160
if "%ACHOICE%"=="5" set ATRAC_SELECTED=1
if "%ACHOICE%"=="6" set ATRACBITRATE=192
if "%ACHOICE%"=="6" set ATRAC_SELECTED=1
if "%ACHOICE%"=="7" set ATRACBITRATE=256
if "%ACHOICE%"=="7" set ATRAC_SELECTED=1
if "%ACHOICE%"=="8" set ATRACBITRATE=320
if "%ACHOICE%"=="8" set ATRAC_SELECTED=1
if "%ACHOICE%"=="9" set ATRACBITRATE=352
if "%ACHOICE%"=="9" set ATRAC_SELECTED=1

:set_atrac_finish
if not defined ATRAC_SELECTED ( echo   ERROR: Choose one of the listed options. & pause & goto set_atrac )
call :normalize_audio_settings
call :save_cfg
echo   [OK] ATRAC Bitrate set to %ATRACBITRATE% Kbps
pause
goto encoding_settings

:set_audio_channels
echo.
echo   1. Mono   (1ch, valid bitrates: 32/48/64/96/128 Kbps)
echo   2. Stereo (2ch, valid bitrates: 48/64/96/128/160/192/256/320/352 Kbps)
echo.
set /p CHANCHOICE=   Choose (current: %AUDIOCHANNELS%ch): 
if "%CHANCHOICE%"=="" goto encoding_settings
if "%CHANCHOICE%"=="1" set AUDIOCHANNELS=1
if "%CHANCHOICE%"=="1" goto audio_channels_chosen
if "%CHANCHOICE%"=="2" set AUDIOCHANNELS=2
if "%CHANCHOICE%"=="2" goto audio_channels_chosen
echo   ERROR: Choose 1 or 2.
pause
goto set_audio_channels
:audio_channels_chosen
call :normalize_audio_settings
call :save_cfg
goto set_atrac

:toggle_source_bitrate
echo.
if "%MATCHSOURCEBITRATE%"=="1" (
    set MATCHSOURCEBITRATE=0
    echo   [OK] Source Bitrate Match set to Off
) else (
    set MATCHSOURCEBITRATE=1
    echo   [OK] Source Bitrate Match set to On
)
call :save_cfg
pause
goto encoding_settings

:reset_defaults
set AVERAGEBITRATE=1000
set MAXBITRATE=2000
set AUDIOCHANNELS=2
set ATRACBITRATE=128
set ENCODEMODE=2pass
set IDRDURATION=2000
set MFRAMES=1
set MATCHSOURCEBITRATE=0
call :save_cfg
echo.
echo   [OK] Values reset to defaults.
pause
goto encoding_settings

:load_cfg
set AVERAGEBITRATE=1000
set MAXBITRATE=2000
set AUDIOCHANNELS=2
set ATRACBITRATE=128
set ENCODEMODE=2pass
set IDRDURATION=2000
set MFRAMES=1
set MATCHSOURCEBITRATE=0
if not exist "%CFGFILE%" exit /b
for /f "usebackq tokens=1,2 delims==" %%a in ("%CFGFILE%") do (
    if /i "%%a"=="AVERAGEBITRATE" set AVERAGEBITRATE=%%b
    if /i "%%a"=="MAXBITRATE"     set MAXBITRATE=%%b
    if /i "%%a"=="AUDIOCHANNELS"   set AUDIOCHANNELS=%%b
    if /i "%%a"=="ATRACBITRATE"   set ATRACBITRATE=%%b
    if /i "%%a"=="ENCODEMODE"     set ENCODEMODE=%%b
    if /i "%%a"=="IDRDURATION"    set IDRDURATION=%%b
    if /i "%%a"=="MFRAMES"        set MFRAMES=%%b
    if /i "%%a"=="MATCHSOURCEBITRATE" set MATCHSOURCEBITRATE=%%b
)
if not "%MATCHSOURCEBITRATE%"=="1" set MATCHSOURCEBITRATE=0
exit /b

:save_cfg
(
    echo AVERAGEBITRATE=%AVERAGEBITRATE%
    echo MAXBITRATE=%MAXBITRATE%
    echo AUDIOCHANNELS=%AUDIOCHANNELS%
    echo ATRACBITRATE=%ATRACBITRATE%
    echo ENCODEMODE=%ENCODEMODE%
    echo IDRDURATION=%IDRDURATION%
    echo MFRAMES=%MFRAMES%
    echo MATCHSOURCEBITRATE=%MATCHSOURCEBITRATE%
) > "%CFGFILE%"
exit /b

:audio_label
if "%AUDIOCHANNELS%"=="1" (
    set "AUDIOCHANNELMODE=Mono"
) else (
    set "AUDIOCHANNELMODE=Stereo"
)
exit /b

:source_bitrate_label
if "%MATCHSOURCEBITRATE%"=="1" (
    set "SOURCEBITRATEMODE=On"
) else (
    set "SOURCEBITRATEMODE=Off"
)
exit /b

:normalize_audio_settings
if not "%AUDIOCHANNELS%"=="1" if not "%AUDIOCHANNELS%"=="2" set AUDIOCHANNELS=2
if "%AUDIOCHANNELS%"=="1" (
    call :is_mono_bitrate "%ATRACBITRATE%"
    if errorlevel 1 set ATRACBITRATE=64
) else (
    call :is_stereo_bitrate "%ATRACBITRATE%"
    if errorlevel 1 set ATRACBITRATE=128
)
exit /b

:is_mono_bitrate
if "%~1"=="32" exit /b 0
if "%~1"=="48" exit /b 0
if "%~1"=="64" exit /b 0
if "%~1"=="96" exit /b 0
if "%~1"=="128" exit /b 0
exit /b 1

:is_stereo_bitrate
if "%~1"=="48" exit /b 0
if "%~1"=="64" exit /b 0
if "%~1"=="96" exit /b 0
if "%~1"=="128" exit /b 0
if "%~1"=="160" exit /b 0
if "%~1"=="192" exit /b 0
if "%~1"=="256" exit /b 0
if "%~1"=="320" exit /b 0
if "%~1"=="352" exit /b 0
exit /b 1

:ensure_workspace
md input 2>nul
md input\pmf 2>nul
md input\video_edited 2>nul
md output 2>nul
md output\video 2>nul
md output\pmf 2>nul
exit /b

:check_tools
set "MISSING_TOOLS="
if not exist "%FF%" set "MISSING_TOOLS=1"
if not exist "%PD%" set "MISSING_TOOLS=1"
if not exist "%OMPS%" set "MISSING_TOOLS=1"
if not exist "%MP%" set "MISSING_TOOLS=1"
if not exist "%GETDURATION%" set "MISSING_TOOLS=1"
if not exist "%AT3%" set "MISSING_TOOLS=1"
if not defined MISSING_TOOLS exit /b 0

cls
echo.
echo  ============================================================
echo   Missing Dependencies
echo  ============================================================
echo.
echo   pmftools-plus only uses tools from this local folder:
echo   %CD%\tools
echo.
echo.
if not exist "%FF%" echo   - %FF%
if not exist "%PD%" echo   - %PD%
if not exist "%OMPS%" echo   - %OMPS%
if not exist "%MP%" echo   - %MP%
if not exist "%GETDURATION%" echo   - %GETDURATION%
if not exist "%AT3%" echo   - %AT3%
echo.
exit /b 1

:pmf2mov
cls
echo.
call :ensure_workspace
if not exist "%FF%" ( echo ERROR: ffmpeg not found. & pause & goto menu )
if not exist "%PD%" ( echo ERROR: psmfdump not found. & pause & goto menu )

set "PMF_TOTAL=0"
for %%f in (input\pmf\*) do (
    if not exist "%%f\" if /i not "%%~nxf"=="desktop.ini" (
        set /a PMF_TOTAL+=1
    )
)
if "%PMF_TOTAL%"=="0" (
    echo No PMF/PSMF files found in input\pmf\
) else (
    echo Found %PMF_TOTAL% file^(s^) to convert.
    echo.

    set "PMF_INDEX=0"
    set "PMF_OK=0"
    set "PMF_FAIL=0"

    for %%f in (input\pmf\*) do (
        if not exist "%%f\" if /i not "%%~nxf"=="desktop.ini" (
            set /a PMF_INDEX+=1
            call :do_pmf2mov "%%f" "!PMF_INDEX!" "%PMF_TOTAL%"
            if errorlevel 1 (
                set /a PMF_FAIL+=1
            ) else (
                set /a PMF_OK+=1
            )
        )
    )

    echo.
    echo Done: !PMF_OK! converted, !PMF_FAIL! failed.
)

rd /s /q obj >nul 2>&1
echo.
pause
goto menu

:do_pmf2mov
set "PROGRESS_INDEX=%~2"
set "PROGRESS_TOTAL=%~3"
if "%PROGRESS_INDEX%"=="" set "PROGRESS_INDEX=1"
if "%PROGRESS_TOTAL%"=="" set "PROGRESS_TOTAL=1"

echo [%PROGRESS_INDEX%/%PROGRESS_TOTAL%] %~nx1
echo   Extracting streams...
md obj 2>nul
md output\video 2>nul

%PD% "%~1" -a "obj\%~n1.oma" -v "obj\%~n1.264" >nul 2>&1

set "FFMPEG_INPUTS=-i ""obj\%~n1.264"""
set "FFMPEG_MAPS=-map 0:v:0"
set "AUDIO_TRACKS=0"

if exist "obj\%~n1.oma" call :add_audio "obj\%~n1.oma"
for /L %%i in (0,1,99) do (
    if exist "obj\%~n1.%%i.oma" call :add_audio "obj\%~n1.%%i.oma"
    if exist "obj\%~n1.%%i.at3" call :add_audio "obj\%~n1.%%i.at3"
)

if !AUDIO_TRACKS! GTR 0 if exist "obj\%~n1.264" (
    if exist "output\video\%~n1.mov" del "output\video\%~n1.mov" >nul 2>&1
    echo   Encoding MOV...
    %FF% %FFFLAGS% !FFMPEG_INPUTS! !FFMPEG_MAPS! -s 480x272 -c:v prores_ks -profile:v 3 "output\video\%~n1.mov"
    if errorlevel 1 (
        echo ERROR: Failed to convert %~nx1
        exit /b 1
    ) else (
        if exist "output\video\%~n1.mov" (
            call :save_source_extension "%~n1" "%~x1"
            call :save_source_video_bitrate "%~n1" "obj\%~n1.264" "%~1"
            call :save_source_audio_settings "%~n1" "obj\%~n1"
            echo [OK] %~n1.mov
			echo.
            exit /b 0
        ) else (
            echo ERROR: Failed to convert %~nx1
            exit /b 1
        )
    )
) else (
    echo ERROR: Failed to extract %~nx1
    exit /b 1
)
exit /b 1

:save_source_extension
call :save_worker_value "SourceExtensions" "%~1" "%~2"
exit /b

:load_source_extension
call :load_worker_value "SourceExtensions" "%~1" SOURCE_EXT ".PMF"
if "%SOURCE_EXT%"=="" set "SOURCE_EXT=.PMF"
exit /b

:save_source_video_bitrate
set "SOURCE_VIDEO_BITRATE="
call :calculate_source_video_bitrate "%~2" "%~3"
if not defined SOURCE_VIDEO_BITRATE exit /b
call :save_worker_value "SourceVideoBitrates" "%~1" "%SOURCE_VIDEO_BITRATE%"
exit /b

:calculate_source_video_bitrate
set "SOURCE_VIDEO_BITRATE="
set "VIDEO_BYTES=0"
for %%s in ("%~1") do set "VIDEO_BYTES=%%~zs"
if "%VIDEO_BYTES%"=="0" exit /b 1

set "DURATION_MIN="
set "DURATION_SEC="
for /f "tokens=1,2 delims=," %%a in ('%GETDURATION% "%~2" 2^>nul') do (
    set "DURATION_MIN=%%a"
    set "DURATION_SEC=%%b"
)
if not defined DURATION_MIN exit /b 1
if not defined DURATION_SEC exit /b 1
echo %DURATION_MIN%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 exit /b 1
echo %DURATION_SEC%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 exit /b 1
set /a DURATION_SECONDS=(DURATION_MIN * 60) + DURATION_SEC
if %DURATION_SECONDS% LEQ 0 exit /b 1
set /a BYTES_PER_SECOND=VIDEO_BYTES / DURATION_SECONDS
set /a SOURCE_VIDEO_BITRATE=((BYTES_PER_SECOND * 8) + 500) / 1000
if %SOURCE_VIDEO_BITRATE% LSS 1 set SOURCE_VIDEO_BITRATE=1
exit /b

:load_source_video_bitrate
set "SOURCE_VIDEO_BITRATE="
call :load_worker_value "SourceVideoBitrates" "%~1" SOURCE_VIDEO_BITRATE ""
if not defined SOURCE_VIDEO_BITRATE exit /b
echo %SOURCE_VIDEO_BITRATE%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 set "SOURCE_VIDEO_BITRATE="
if not defined SOURCE_VIDEO_BITRATE exit /b
set /a SOURCE_VIDEO_BITRATE=SOURCE_VIDEO_BITRATE
if %SOURCE_VIDEO_BITRATE% LSS 1 set "SOURCE_VIDEO_BITRATE="
exit /b

:save_source_audio_settings
set "SOURCE_AUDIO_FOUND=0"
set "SOURCE_AUDIO_MISMATCH="
set "SOURCE_AUDIO_BITRATE="
set "SOURCE_AUDIO_CHANNELS="

call :collect_source_audio_file "%~2.oma"
for /L %%i in (0,1,99) do (
    call :collect_source_audio_file "%~2.%%i.oma"
    call :collect_source_audio_file "%~2.%%i.at3"
)

if not "%SOURCE_AUDIO_FOUND%"=="1" exit /b
if defined SOURCE_AUDIO_MISMATCH exit /b

call :save_worker_value "SourceAudioBitrates" "%~1" "%SOURCE_AUDIO_BITRATE%"
call :save_worker_value "SourceAudioChannels" "%~1" "%SOURCE_AUDIO_CHANNELS%"
exit /b

:collect_source_audio_file
if not exist "%~1" exit /b
call :probe_source_audio_file "%~1"
if not defined PROBE_AUDIO_BITRATE (
    set "SOURCE_AUDIO_MISMATCH=1"
    exit /b
)
if not defined PROBE_AUDIO_CHANNELS (
    set "SOURCE_AUDIO_MISMATCH=1"
    exit /b
)

if "%SOURCE_AUDIO_FOUND%"=="0" (
    set "SOURCE_AUDIO_FOUND=1"
    set "SOURCE_AUDIO_BITRATE=%PROBE_AUDIO_BITRATE%"
    set "SOURCE_AUDIO_CHANNELS=%PROBE_AUDIO_CHANNELS%"
) else (
    if not "%SOURCE_AUDIO_BITRATE%"=="%PROBE_AUDIO_BITRATE%" set "SOURCE_AUDIO_MISMATCH=1"
    if not "%SOURCE_AUDIO_CHANNELS%"=="%PROBE_AUDIO_CHANNELS%" set "SOURCE_AUDIO_MISMATCH=1"
)
exit /b

:probe_source_audio_file
set "PROBE_AUDIO_BITRATE="
set "PROBE_AUDIO_CHANNELS="
set "AUDIO_PROBE_LINE="
set "AUDIO_PROBE_CHANNEL="
set "AUDIO_PROBE_RATE="

for /f "delims=" %%p in ('%FF% -hide_banner -i "%~1" 2^>^&1 ^| findstr /i /c:"Audio:"') do (
    if not defined AUDIO_PROBE_LINE set "AUDIO_PROBE_LINE=%%p"
)
if not defined AUDIO_PROBE_LINE exit /b 1

for /f "tokens=3,5 delims=," %%a in ("!AUDIO_PROBE_LINE!") do (
    set "AUDIO_PROBE_CHANNEL=%%a"
    set "AUDIO_PROBE_RATE=%%b"
)

set "AUDIO_PROBE_CHANNEL=!AUDIO_PROBE_CHANNEL: =!"
if /i "!AUDIO_PROBE_CHANNEL!"=="mono" set "PROBE_AUDIO_CHANNELS=1"
if /i "!AUDIO_PROBE_CHANNEL!"=="stereo" set "PROBE_AUDIO_CHANNELS=2"

for /f "tokens=1" %%r in ("!AUDIO_PROBE_RATE!") do set "PROBE_AUDIO_BITRATE=%%r"
if not defined PROBE_AUDIO_BITRATE exit /b 1
echo !PROBE_AUDIO_BITRATE!| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 set "PROBE_AUDIO_BITRATE="
if not defined PROBE_AUDIO_BITRATE exit /b 1

if "%PROBE_AUDIO_CHANNELS%"=="1" (
    call :is_mono_bitrate "%PROBE_AUDIO_BITRATE%"
) else (
    if "%PROBE_AUDIO_CHANNELS%"=="2" (
        call :is_stereo_bitrate "%PROBE_AUDIO_BITRATE%"
    ) else (
        exit /b 1
    )
)
if errorlevel 1 (
    set "PROBE_AUDIO_BITRATE="
    set "PROBE_AUDIO_CHANNELS="
    exit /b 1
)
exit /b

:load_source_audio_settings
set "SOURCE_AUDIO_BITRATE="
set "SOURCE_AUDIO_CHANNELS="
call :load_worker_value "SourceAudioBitrates" "%~1" SOURCE_AUDIO_BITRATE ""
call :load_worker_value "SourceAudioChannels" "%~1" SOURCE_AUDIO_CHANNELS ""
if not defined SOURCE_AUDIO_BITRATE exit /b
if not defined SOURCE_AUDIO_CHANNELS exit /b

echo %SOURCE_AUDIO_BITRATE%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 set "SOURCE_AUDIO_BITRATE="
echo %SOURCE_AUDIO_CHANNELS%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 set "SOURCE_AUDIO_CHANNELS="
if not defined SOURCE_AUDIO_BITRATE exit /b
if not defined SOURCE_AUDIO_CHANNELS exit /b

if "%SOURCE_AUDIO_CHANNELS%"=="1" (
    call :is_mono_bitrate "%SOURCE_AUDIO_BITRATE%"
) else (
    if "%SOURCE_AUDIO_CHANNELS%"=="2" (
        call :is_stereo_bitrate "%SOURCE_AUDIO_BITRATE%"
    ) else (
        set "SOURCE_AUDIO_BITRATE="
        set "SOURCE_AUDIO_CHANNELS="
        exit /b
    )
)
if errorlevel 1 (
    set "SOURCE_AUDIO_BITRATE="
    set "SOURCE_AUDIO_CHANNELS="
)
exit /b

:save_worker_value
set "WORKER_SECTION=%~1"
set "WORKER_KEY=%~2"
set "WORKER_VALUE=%~3"
md obj 2>nul
set "WORKER_TMP=obj\%WORKERFILE%.tmp"
set "IN_WORKER_SECTION=0"
set "FOUND_WORKER_SECTION=0"
set "FOUND_WORKER_KEY=0"

if exist "%WORKER_TMP%" del "%WORKER_TMP%" >nul 2>&1

if exist "%WORKERFILE%" (
    for /f "usebackq delims=" %%l in ("%WORKERFILE%") do (
        set "WORKER_LINE=%%l"
        if "!WORKER_LINE:~0,1!"=="[" (
            if "!IN_WORKER_SECTION!"=="1" if "!FOUND_WORKER_KEY!"=="0" (
                >> "%WORKER_TMP%" echo %WORKER_KEY%=%WORKER_VALUE%
                set "FOUND_WORKER_KEY=1"
            )

            set "IN_WORKER_SECTION=0"
            if /i "!WORKER_LINE!"=="[%WORKER_SECTION%]" (
                set "IN_WORKER_SECTION=1"
                set "FOUND_WORKER_SECTION=1"
            )
            >> "%WORKER_TMP%" echo !WORKER_LINE!
        ) else (
            if "!IN_WORKER_SECTION!"=="1" (
                set "WORKER_LINE_KEY="
                for /f "tokens=1,* delims==" %%a in ("!WORKER_LINE!") do set "WORKER_LINE_KEY=%%a"
                if /i "!WORKER_LINE_KEY!"=="%WORKER_KEY%" (
                    if "!FOUND_WORKER_KEY!"=="0" >> "%WORKER_TMP%" echo %WORKER_KEY%=%WORKER_VALUE%
                    set "FOUND_WORKER_KEY=1"
                ) else (
                    >> "%WORKER_TMP%" echo !WORKER_LINE!
                )
            ) else (
                >> "%WORKER_TMP%" echo !WORKER_LINE!
            )
        )
    )
)

if "%FOUND_WORKER_SECTION%"=="0" (
    if exist "%WORKER_TMP%" >> "%WORKER_TMP%" echo.
    >> "%WORKER_TMP%" echo [%WORKER_SECTION%]
    >> "%WORKER_TMP%" echo %WORKER_KEY%=%WORKER_VALUE%
) else (
    if "%IN_WORKER_SECTION%"=="1" if "%FOUND_WORKER_KEY%"=="0" >> "%WORKER_TMP%" echo %WORKER_KEY%=%WORKER_VALUE%
)

move /y "%WORKER_TMP%" "%WORKERFILE%" >nul
exit /b

:load_worker_value
set "WORKER_SECTION=%~1"
set "WORKER_KEY=%~2"
set "%~3=%~4"
set "IN_WORKER_SECTION=0"
if not exist "%WORKERFILE%" exit /b
for /f "usebackq delims=" %%l in ("%WORKERFILE%") do (
    set "WORKER_LINE=%%l"
    if "!WORKER_LINE:~0,1!"=="[" (
        set "IN_WORKER_SECTION=0"
        if /i "!WORKER_LINE!"=="[%WORKER_SECTION%]" set "IN_WORKER_SECTION=1"
    ) else (
        if "!IN_WORKER_SECTION!"=="1" (
            for /f "tokens=1,* delims==" %%a in ("!WORKER_LINE!") do (
                if /i "%%a"=="%WORKER_KEY%" set "%~3=%%b"
            )
        )
    )
)
exit /b

:add_audio
set /a AUDIO_TRACKS+=1
set "FFMPEG_INPUTS=!FFMPEG_INPUTS! -i ""%~1"""
set "FFMPEG_MAPS=!FFMPEG_MAPS! -map !AUDIO_TRACKS!:a:0"
exit /b

:video2pmf
cls
echo.
call :ensure_workspace
if not exist "%FF%" ( echo ERROR: ffmpeg not found. & pause & goto menu )
if not exist "%OMPS%" ( echo ERROR: oMPSComposer.exe not found. & pause & goto menu )
if not exist "%MP%" ( echo ERROR: mps2pmf not found. & pause & goto menu )
if not exist "%GETDURATION%" ( echo ERROR: get_duration not found. & pause & goto menu )

set "found_video=0"
for %%f in (input\video_edited\*.mov input\video_edited\*.avi input\video_edited\*.mp4) do (
    set "found_video=1"
    call :do_video2pmf "%%f"
)
if "%found_video%"=="0" echo No .mov, .avi, or .mp4 files found in input\video_edited\

rd /s /q obj >nul 2>&1
echo.
pause
goto menu

:do_video2pmf
echo --- Converting %~nx1 to PMF ---
md obj 2>nul
md output\pmf 2>nul

set "JOBDIR=obj\%~n1"
md "!JOBDIR!" 2>nul

set "AUDIO_WAVS="
set "AUDIO_TRACKS=0"
call :prepare_audio_settings "%~n1"

for /f "delims=" %%a in ('%FF% -hide_banner -i "%~1" 2^>^&1 ^| findstr /i /c:"Audio:"') do (
    set /a AUDIO_TRACKS+=1
)

if !AUDIO_TRACKS! EQU 0 ( echo ERROR: No audio in %~nx1 & exit /b )

for /L %%i in (0,1,15) do (
    %FF% %FFFLAGS% -i "%~1" -map 0:a:%%i -ar 44100 -ac !ENC_AUDIO_CHANNELS! -c:a pcm_s16le "!JOBDIR!\audio%%i.wav" >nul 2>&1
    if exist "!JOBDIR!\audio%%i.wav" (
        if "!AUDIO_WAVS!"=="" (
            set "AUDIO_WAVS=!JOBDIR!\audio%%i.wav"
        ) else (
            set "AUDIO_WAVS=!AUDIO_WAVS!,!JOBDIR!\audio%%i.wav"
        )
    )
)

set "MPS_PATH=!JOBDIR!\%~n1.MPS"
call :load_source_extension "%~n1"
set "PMF_PATH=output\pmf\%~n1!SOURCE_EXT!"
call :prepare_video_bitrates "%~n1"

"%OMPS%" "%~1" "!AUDIO_WAVS!" "!MPS_PATH!" --avg-bitrate !ENC_AVG! --max-bitrate !ENC_MAX! --audio-bitrate !ENC_AUDIO_BITRATE! --encode-mode %ENCODEMODE% --idr-duration %IDRDURATION% --m-frames %MFRAMES%
if not exist "!MPS_PATH!" ( echo ERROR: oMPSComposer failed for %~nx1 & exit /b )

for /f "tokens=1,2 delims=," %%a in ('%GETDURATION% "!MPS_PATH!"') do (
    set min=%%a
    set sec=%%b
)

%MP% -i "!MPS_PATH!" -o "!PMF_PATH!" -m !min! -s !sec! >nul 2>&1

if errorlevel 1 ( echo ERROR: mps2pmf failed for %~nx1 & exit /b )

echo [BUILDED] %~n1!SOURCE_EXT!
echo.
exit /b

:prepare_video_bitrates
set "ENC_AVG=%AVERAGEBITRATE%"
set "ENC_MAX=%MAXBITRATE%"

if not "%MATCHSOURCEBITRATE%"=="1" exit /b

call :load_source_video_bitrate "%~1"
if not defined SOURCE_VIDEO_BITRATE exit /b

set /a ENC_AVG=SOURCE_VIDEO_BITRATE
set /a CFG_AVG=AVERAGEBITRATE
if !CFG_AVG! LEQ 0 set /a CFG_AVG=500

set /a ENC_MAX=((ENC_AVG * MAXBITRATE) + (CFG_AVG / 2)) / CFG_AVG

if !ENC_AVG! LSS 1 set /a ENC_AVG=1
if !ENC_MAX! LEQ !ENC_AVG! set /a ENC_MAX=ENC_AVG + 1
if !ENC_MAX! GEQ 4800 set /a ENC_MAX=4799
if !ENC_AVG! GEQ !ENC_MAX! set /a ENC_AVG=ENC_MAX - 1
if !ENC_AVG! LSS 1 set /a ENC_AVG=1

echo   [BITRATE] Using source video bitrate: avg !ENC_AVG! kbps / max !ENC_MAX! kbps
exit /b

:prepare_audio_settings
set "ENC_AUDIO_CHANNELS=%AUDIOCHANNELS%"
set "ENC_AUDIO_BITRATE=%ATRACBITRATE%"

if not "%MATCHSOURCEBITRATE%"=="1" exit /b

call :load_source_audio_settings "%~1"
if not defined SOURCE_AUDIO_BITRATE exit /b
if not defined SOURCE_AUDIO_CHANNELS exit /b

set "ENC_AUDIO_CHANNELS=%SOURCE_AUDIO_CHANNELS%"
set "ENC_AUDIO_BITRATE=%SOURCE_AUDIO_BITRATE%"

if "%ENC_AUDIO_CHANNELS%"=="1" (
    set "ENC_AUDIO_LABEL=mono"
) else (
    set "ENC_AUDIO_LABEL=stereo"
)
echo   [BITRATE] Using source audio: !ENC_AUDIO_BITRATE! kbps / !ENC_AUDIO_LABEL!
exit /b
