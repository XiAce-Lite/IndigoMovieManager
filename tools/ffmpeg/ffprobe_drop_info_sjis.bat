@echo off
setlocal

rem ffprobe.exe と同じフォルダに置いて、動画をドラッグ&ドロップして使う
set "FFPROBE=%~dp0ffprobe.exe"

if not exist "%FFPROBE%" (
    echo [ERROR] ffprobe.exe が見つかりません。
    echo この BAT を ffprobe.exe と同じフォルダに置いてください。
    echo.
    pause
    exit /b 1
)

rem 引数が無い時は使い方だけ表示して終了する
if "%~1"=="" (
    echo [使い方] 動画ファイルをこの BAT にドラッグ&ドロップしてください。
    echo.
    pause
    exit /b 1
)

:loop
if "%~1"=="" goto end

rem 渡された動画ごとに ffprobe の情報をそのまま表示する
if exist "%~f1" (
    echo ============================================================
    echo 対象: %~f1
    echo ============================================================
    "%FFPROBE%" "%~f1"
) else (
    echo [WARN] ファイルが見つかりません: %~1
)

echo.
shift
goto loop

:end
pause
endlocal
