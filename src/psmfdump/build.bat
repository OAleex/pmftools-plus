@echo off
dotnet publish psmfdump.csproj -c Release -r win-x64 --self-contained true && echo Done.
pause
