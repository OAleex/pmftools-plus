@echo off
msbuild Mps2Pmf.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v145 /t:Rebuild /nologo /v:minimal && echo Done.
pause
