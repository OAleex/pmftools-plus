@echo off
pip install pyinstaller pymediainfo >nul
pyinstaller --onefile get_duration.py && echo Done.
pause
