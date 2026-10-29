@echo off
rem Builds the Bannerlord module and copies it into the game's Modules folder.
rem Usage: build_bannerlord.bat ["C:\path\to\Mount & Blade II Bannerlord"]
setlocal
set "GAME=%~1"
if "%GAME%"=="" set "GAME=Z:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
cd /d "%~dp0"
dotnet build -c Release -nologo "-p:BannerlordDir=%GAME%" || exit /b 1
set "DEST=%GAME%\Modules\ColoredPoliticalRegionsBorders"
if not exist "%DEST%\bin\Win64_Shipping_Client" mkdir "%DEST%\bin\Win64_Shipping_Client"
copy /y "Module\SubModule.xml" "%DEST%\SubModule.xml" >nul || exit /b 1
copy /y "bin\Release\ColoredPoliticalRegionsBorders.dll" "%DEST%\bin\Win64_Shipping_Client\" >nul || exit /b 1
echo Installed into "%DEST%"
