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
echo   Instalacion y Configuracion - Agente FiscalPrinter
echo =========================================================
echo.

:: 2. Limpieza de instalaciones previas o servicios huérfanos
echo [1/8] Deteniendo y limpiando configuraciones anteriores...
taskkill /F /IM ImpresionFiscal.exe >nul 2>&1
sc.exe stop FiscalPrinter >nul 2>&1
sc.exe delete FiscalPrinter >nul 2>&1
schtasks /delete /tn "FiscalPrinterAgent" /f >nul 2>&1

:: 3. Descompresión y Reemplazo Total (Aplanando carpetas anidadas)
echo [2/8] Verificando presencia de paquete ZIP...
if exist "C:\FiscalPrinter.zip" (
    echo Encontrado C:\FiscalPrinter.zip. Procesando actualizacion...

    :: Limpiar carpeta temporal previa si existiera
    if exist "C:\FiscalPrinter_Temp" rmdir /S /Q "C:\FiscalPrinter_Temp"

    :: Extraer en carpeta temporal
    powershell -Command "Expand-Archive -Path 'C:\FiscalPrinter.zip' -DestinationPath 'C:\FiscalPrinter_Temp' -Force"
    
    if !errorlevel! equ 0 (
        :: Borrar la carpeta previa de la API para garantizar reemplazo total
        if exist "C:\FiscalPrinter" rmdir /S /Q "C:\FiscalPrinter"

        :: Evaluar si la extraccion creo una subcarpeta anidada FiscalPrinter
        if exist "C:\FiscalPrinter_Temp\FiscalPrinter\ImpresionFiscal.exe" (
            echo Aplanando estructura de carpetas anidadas...
            move "C:\FiscalPrinter_Temp\FiscalPrinter" "C:\FiscalPrinter" >nul
            rmdir /S /Q "C:\FiscalPrinter_Temp" >nul 2>&1
        ) else (
            ren "C:\FiscalPrinter_Temp" "FiscalPrinter"
        )
        
        :: Eliminar el .zip tras la extracción exitosa
        del /F /Q "C:\FiscalPrinter.zip" >nul 2>&1
        
        echo [OK] Carpeta C:\FiscalPrinter actualizada correctamente.
    ) else (
        echo [ERROR] Ocurrio un fallo al intentar extraer C:\FiscalPrinter.zip.
    )
) else (
    echo No se encontro C:\FiscalPrinter.zip. Se conservara la instalacion existente.
)

:: 4. Configurar regla en Windows Firewall (Puerto 7249 TCP)
echo [3/8] Configurando regla de Firewall en puerto 7249...
netsh advfirewall firewall delete rule name="FiscalPrinter API" >nul 2>&1
netsh advfirewall firewall add rule name="FiscalPrinter API" dir=in action=allow protocol=TCP localport=7249 profile=any >nul 2>&1

:: 5. Registrar el certificado SSL autofirmado en la máquina cliente
echo [4/8] Importando certificado SSL en el almacen de confianza...
if exist "C:\FiscalPrinter\localhost.pfx" (
    certutil -f -p 1234 -importpfx Root "C:\FiscalPrinter\localhost.pfx" NoRoot >nul 2>&1
    echo [OK] Certificado SSL importado correctamente.
) else (
    echo [AVISO] No se encontro C:\FiscalPrinter\localhost.pfx.
)

:: 6. Autorizar el acceso a la red local desde el front (Chrome 142+ / Local Network Access)
::    Chrome 142 bloquea que un sitio servido desde una IP publica (dominio de Cloudflare)
::    alcance loopback o IPs privadas. El bloqueo se reporta como error de CORS, pero el
::    request nunca llega a Kestrel: no hay configuracion de servidor que lo resuelva.
::    Se autoriza el ORIGEN QUE PIDE (el front), no el endpoint local.
::    Para habilitar otro dominio: definir LNA_ORIGEN_2 y repetir los "reg add" con /v 2.
echo [5/8] Autorizando acceso a la red local en los navegadores...

set "LNA_ORIGEN_1=https://[*.]elzyra.com"

set "LNA_CHROME=HKLM\SOFTWARE\Policies\Google\Chrome\LocalNetworkAccessAllowedForUrls"
set "LNA_EDGE=HKLM\SOFTWARE\Policies\Microsoft\Edge\LocalNetworkAccessAllowedForUrls"

:: Limpiar entradas previas para que un reinstalar con otros dominios no deje residuos
reg delete "%LNA_CHROME%" /f >nul 2>&1
reg delete "%LNA_EDGE%"   /f >nul 2>&1

reg add "%LNA_CHROME%" /v 1 /t REG_SZ /d "%LNA_ORIGEN_1%" /f >nul 2>&1
if !errorlevel! equ 0 (echo [OK] Chrome autorizado para %LNA_ORIGEN_1%) else (echo [AVISO] No se pudo escribir la politica de Chrome.)

reg add "%LNA_EDGE%" /v 1 /t REG_SZ /d "%LNA_ORIGEN_1%" /f >nul 2>&1
if !errorlevel! equ 0 (echo [OK] Edge autorizado para %LNA_ORIGEN_1%) else (echo [AVISO] No se pudo escribir la politica de Edge.)

:: 7. Crear Tarea Programada e Iniciar Proceso
echo [6/8] Registrando inicio automatico al iniciar sesion...
schtasks /create /tn "FiscalPrinterAgent" /tr "\"C:\FiscalPrinter\ImpresionFiscal.exe\"" /sc onlogon /rl highest /f >nul 2>&1

echo [7/8] Levantando el agente de impresion...
start "" "C:\FiscalPrinter\ImpresionFiscal.exe"

:: 8. Validar respuesta HTTPS local del endpoint Ping
echo [8/8] Verificando estado del servicio HTTPS...
set "PING_OK=0"
for /l %%i in (1,1,6) do (
    timeout /t 1 /nobreak >nul
    powershell -Command "[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}; (Invoke-WebRequest -Uri 'https://localhost:7249/api/Impresion/Ping' -UseBasicParsing -TimeoutSec 2).StatusCode" 2>nul | findstr /R "^200" >nul
    if !errorlevel! equ 0 (
        set "PING_OK=1"
        goto :verificado
    )
)

:verificado
echo.
if "!PING_OK!"=="1" (
    echo =========================================================
    echo  INSTALACION COMPLETADA: API activa y respondiendo en 7249
    echo =========================================================
) else (
    echo =========================================================
    echo  AVISO: El proceso inicio, pero el Ping HTTPS no respondio.
    echo  Revisa la ruta "C:\FiscalPrinter\ImpresionFiscal.exe".
    echo =========================================================
)

echo.
echo.
echo ---------------------------------------------------------
echo  IMPORTANTE: cerra Chrome y Edge por completo y volve a
echo  abrirlos. La politica de red local no se aplica hasta que
echo  el navegador se reinicia.
echo  Para verificar: abrir chrome://policy y buscar
echo  LocalNetworkAccessAllowedForUrls.
echo ---------------------------------------------------------
timeout /t 5
exit /b
