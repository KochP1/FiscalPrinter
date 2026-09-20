@echo off
setlocal EnableDelayedExpansion

:: 1. Solicitar y elevar privilegios de Administrador
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Elevando permisos de administrador...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo =========================================================
echo   Matar Agente FiscalPrinter (Puerto 7249)
echo =========================================================
echo.

set "PUERTO=7249"
set "PID_ENCONTRADO="

:: Eliminar tarea programada
schtasks /end /tn "FiscalPrinterAgent"
schtasks /delete /tn "FiscalPrinterAgent"

:: 2. Buscar el PID asociado al puerto 7249 en estado LISTENING
for /f "tokens=5" %%a in ('netstat -aon ^| findstr /R /C:":%PUERTO% .*LISTENING"') do (
    set "PID_ENCONTRADO=%%a"
)

:: 3. Evaluar y ejecutar Taskkill
if defined PID_ENCONTRADO (
    if "!PID_ENCONTRADO!"=="0" (
        echo [AVISO] El puerto %PUERTO% esta reservado por el Sistema ^(PID 0^).
    ) else (
        echo [INFO] Proceso encontrado en el puerto %PUERTO% con PID: !PID_ENCONTRADO!
        echo Deteniendo proceso...
        taskkill /PID !PID_ENCONTRADO! /F
        
        if !errorLevel! equ 0 (
            echo [OK] El proceso !PID_ENCONTRADO! ha sido finalizado correctamente.
        ) else (
            echo [ERROR] No se pudo finalizar el proceso !PID_ENCONTRADO!.
        )
    )
) else (
    echo [INFO] No hay ningun proceso escuchando en el puerto %PUERTO%.
)

echo.
timeout /t 3
exit /b