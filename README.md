# Documentación ApiPrinterFiscal

---

## 🔧 Información General

| Propiedad              | Valor                                     |
| ---------------------- | ----------------------------------------- |
| **Título**             | API de Impresora Fiscal                   |
| **Versión**            | v1.26.0903                                |
| **Base URL (fiscal)**  | `http://<host>:7249/api/Impresion`        |
| **Base URL (ticket)**  | `http://<host>:7249/api/ImpresionTicket`  |
| **Puerto por Defecto** | 7249                                      |
| **Framework**          | .NET 10.0                                 |
| **Arquitectura**       | x86 (Forzado para compatibilidad con DLL) |
| **Formato**            | JSON                                      |

### 📌 Descripción
API local del punto de venta que gestiona **dos dominios de impresión**:

1. **Impresión fiscal** (documentos legales) en impresoras fiscales HKA vía la DLL `tfhkaif.dll`:
   facturas, notas de crédito y reportes X/Z.
2. **Impresión de tickets no fiscales** (ESC/POS) en impresoras térmicas USB o de red, más la
   **información del equipo** (`PcInfo`).

---
## ⚙️ Requisitos Técnicos

### 🖥️ Sistema Operativo
- **Windows** (Servicio Windows soportado)
- Arquitectura: **x86** (32 bits)

### 📦 Dependencias
- `Microsoft.AspNetCore.OpenApi` (v10.0.9)
- `Microsoft.Extensions.Hosting.WindowsServices` (v10.0.10)
- `Swashbuckle.AspNetCore` (v10.2.3)
- `System.IO.Ports` (v8.0.0) — puerto COM de la impresora fiscal
- `RawPrint.NetStd` (v1.0.0) — impresión RAW a impresoras USB de Windows
- `System.Management` (v7.0.2) — detección de impresoras de Windows (WMI)

## 📁 Estructura de Archivos del Proyecto

```
ImpresionFiscal/
├── Properties/
│   └── launchSettings.json
├── Controllers/
│   ├── ImpresionController.cs         # Impresión fiscal (factura, NC, reportes X/Z)
│   ├── ImpresionTicketController.cs   # Tickets no fiscales + PcInfo (migrado de apireslocal)
│   └── WeatherForecastController.cs
├── Services/
│   └── PrinterService.cs             # Térmica USB/red, info de red, impresoras Windows,
│                                     #   detección del COM fiscal y cambio de predeterminada
├── DLLs/
│   └── tfhkaif.dll
├── Dtos/
│   ├── ConectarRequest.cs
│   ├── DiagnosticoDllDtos.cs
│   ├── EstadoDetallado.cs
│   ├── FacturaDtos.cs                # FacturaFiscal, NotaCredito, FormasPagos, RegistrosCierresValidarZ
│   ├── ImpresionRes.cs
│   ├── PuertoFiscal.cs               # PuertoSerieInfo, PuertoFiscalInfo, ModeloFiscalConfig
│   ├── ReporteRequest.cs
│   └── Ticket.cs                     # ImpresionRequest, ImpresionIPRequest, ImpresoraInfo,
│                                     #   ImpresoraPredeterminadaRequest/Res, ResultInit
├── appsettings.json
├── appsettings.Development.json
├── ImpresionFiscal.http
└── Program.cs
```

## 🌐 Endpoints Disponibles

### 🧾 Impresión fiscal — `api/Impresion`

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET  | `/Ping` | Verifica que la API responde. |
| GET  | `/Estado/{puerto}` | Estado de la impresora (frame S1). |
| GET  | `/EstadoDetallado/{puerto}` | Estado extendido (S1 + S2 + flags). |
| GET  | `/checkDll` | Diagnóstico de carga de la DLL fiscal. |
| POST | `/conectar/{puerto}` | Abre el puerto COM y valida la conexión. |
| POST | `/imprimirFact` | Imprime una factura. **Devuelve `numFiscal`.** |
| POST | `/ImprimirNcr` | Imprime una nota de crédito. **Devuelve `numFiscal`.** |
| POST | `/ImprimirReporte/{puerto}` | Reporte **X** o **Z** (el número Z se lee del frame S1). |
| POST | `/reconciliarZ` | Reconcilia cierres Z contra la impresora. |
| POST | `/PruebaFact`, `/PruebaNcr` | Endpoints de prueba (no cierran documento real). |

### 🖨️ Tickets no fiscales — `api/ImpresionTicket`

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET  | `/printers` | Lista **todas** las impresoras instaladas en Windows, cada una con su bandera `default`. |
| GET  | `/PcInfo` | `Equipo` (hostname) + `IpResultante` (IPs) + `impresoras` (predeterminada) + `PuertoFiscal`, `ImpresoraFiscal` y `PuertoFiscalDetalle`. |
| GET  | `/Equipo` | Solo el hostname: `{ equipo }`. Endpoint liviano, no consulta impresoras ni red. |
| GET  | `/PuertoFiscal` | En qué COM está la impresora fiscal, y sus candidatos. **No abre ningún puerto.** |
| PUT  | `/ImpresoraPredeterminada` | Cambia la impresora predeterminada del equipo (`{ Nombre }`). Responde **200 con `exito: false`** y el motivo cuando no se puede, para que el front muestre el estado real. |
| POST | `/Imprimir` | Imprime ESC/POS a una impresora **USB** por nombre (`{ PrinterName, EscPosCode }`). |
| POST | `/ImprimirIP` | Imprime ESC/POS a una impresora de **red** por TCP **9100** (`{ PrinterIp, EscPosCode }`, timeout 5 s). |
| POST | `/ImprimirAuto` | Imprime ESC/POS **resolviendo la impresora predeterminada del equipo** (`{ EscPosCode }`). Si tiene IP válida → TCP 9100; si no → USB por nombre. |

> ⚠️ **`ImprimirAuto` resuelve la impresora de la máquina que ATIENDE el request**, no de la que lo
> origina. En **cobros** el front apunta a la **IP de la estación remota** (imprime allá); en
> **devoluciones** apunta a `localhost` (imprime en el equipo del cajero).

> El **ESC/POS lo arma la API central de Elzyra (C#)** y llega **en Base64**; este servicio solo lo
> decodifica y lo manda RAW a la impresora. No genera ni formatea el contenido.

---

## 🔢 El correlativo fiscal y el frame S1

Tras finalizar el documento (con el puerto **aún abierto**), la API vuelca el **frame de estado S1** de
la impresora a un archivo (`UploadStatusCmd(..., "S1", ...)`) y extrae el correlativo por offset fijo:

| Documento | Offset (base 0) | Campo |
|-----------|-----------------|-------|
| Factura   | `LeerCampoS1(21, 8)` | Número de factura fiscal |
| Nota de crédito | `LeerCampoS1(47, 8)` | Contador de notas de crédito |
| Reporte Z | `LeerCampoS1(60, 4)` | Número de reporte Z |

`imprimirFact` e `ImprimirNcr` devuelven ese valor en `numFiscal`; en caso de error, `numFiscal = null`.

> El **serial** de la impresora también está en el frame, pero no se usa acá: lo resuelve la API central
> a partir de la estación del turno.

---

## 🔌 Cómo se detecta la impresora fiscal

El modelo **no se puede leer del puerto**. Medido sobre una HKA80 conectada (2026-09-10), todo lo que
Windows sabe de ese COM sale de su driver genérico:

```
DeviceDesc   : @usbser.inf,%usbserial.devicedesc%;Dispositivo serie USB
FriendlyName : Dispositivo serie USB (COM5)
Mfg          : @usbser.inf,%msft%;Microsoft
HardwareID   : USB\VID_04D8&PID_000A
```

El puente CDC no publica el modelo. Lo que **sí** identifica el cable es el **VID/PID**, y es estable
entre reinicios y máquinas aunque Windows reasigne el número de COM.

Por eso cada modelo se declara en `appsettings.json` y se reconoce por hardware primero, por texto
después:

```json
"PuertoFiscal": {
  "Modelos": [
    {
      "Nombre": "HKA80",
      "IdsHardware": [ "VID_04D8&PID_000A" ],
      "Firmas": [ "hka", "tfhka", "the factory" ]
    }
  ]
}
```

- **`IdsHardware`** se busca por contenido dentro del `DeviceID` (el último tramo es la instancia y
  cambia por puerto, así que no sirve comparar por igualdad). Se prueba en **todos** los modelos antes
  que cualquier firma: un acierto por hardware vale más que uno por nombre de driver.
- **`Firmas`** son textos que se buscan en la descripción y el fabricante. Solo sirven cuando el
  dispositivo trae un driver propio que se identifica.

El resultado dice **cómo** se decidió, en `origen`:

| `origen` | Significa |
|---|---|
| `id-hardware` | Coincidió el VID/PID. Es el caso bueno. |
| `firma` | Coincidió un texto de la descripción o el fabricante. |
| `unico-puerto` | No coincidió nada, pero hay un solo COM en la máquina. **Verificar antes de guardarlo.** |
| `ambiguo` | Varios COM y ninguno decidible. `puerto` viene en `null` y hay que elegir de `candidatos`. |
| `sin-puertos` | La máquina no tiene puertos serie. |

> ⚠️ `VID_04D8&PID_000A` es de un **CDC de Microchip** y **no es exclusivo de HKA**: cualquier equipo
> con ese firmware sin personalizar lo comparte. Si en una caja aparece otro, la detección pasa a
> `ambiguo` y ofrece elegir — **no elige mal en silencio**.

> `candidatos` **no es el inventario de puertos** de la máquina: cuando la fiscal se reconoce trae un
> solo elemento. Trae varios solo cuando no se pudo decidir, porque ahí la lista *es* la respuesta.

---

## 📋 Tablas de referencia

**Códigos de Estado (S1):**
| Código | Descripción |
|--------|-------------|
| 0 | READY - Impresora lista |
| 1 | MODO_ENTRENAMIENTO |
| 2 | DOCUMENTO_NO_FISCAL |
| 3 | FACTURA_ABIERTA |
| 4 | NOTA_CREDITO_ABIERTA |
| 5 | NOTA_DEBITO_ABIERTA |
| 6 | MEMORIA_LLENA |
| 7 | CIERRE_Z_REQUERIDO |

**Tipos de IVA:**
| Código | Descripción |
|--------|-------------|
| 1 | Exento (!) |
| 2 | 8% (") |
| 3 | 31% (#) |
| otro | General (espacio) |

**Códigos Fiscales para Medios de Pago:**
| Código | Descripción |
|--------|-------------|
| 01 | Efectivo Bs |
| 02 | Cheque Bs |
| 03 | Punto de Venta |
| 04-19 | Otros medios Bs |
| 20-24 | Divisas (con IGTF) |

**Tipos de Impuesto para Devolución:**
| Código | Comando |
|--------|---------|
| 1 | d1 (Exento) |
| 2 | d2 (8%) |
| 3 | d3 (31%) |
| otro | d0 (General) |

---

## 🚨 Códigos de Estado y Errores

### 🔴 Errores Comunes

| Error | Causa | Solución |
|-------|-------|----------|
| `No se logró conectar al puerto` | Puerto COM incorrecto o impresora apagada | Verificar conexión física y puerto |
| `Error enviando RIF` | RIF inválido o impresora no lista | Verificar formato del RIF |
| `Error en el artículo` | Descripción muy larga o formato inválido | Limitar descripción a 37 caracteres |
| `CIERRE_Z_REQUERIDO` | La impresora requiere cierre Z | Ejecutar reporte Z antes de continuar |
| `DOCUMENTO_NO_FISCAL` | La impresora no está en modo fiscal | Reiniciar impresora |
| `MEMORIA_LLENA` | Memoria de la impresora llena | Realizar cierre Z |

### 📊 Respuestas HTTP

| Código | Descripción |
|--------|-------------|
| 200 | OK - Operación exitosa |
| 500 | Error interno del servidor |

**Nota:** Los errores de negocio de la impresión fiscal se devuelven como **200 OK** con `status: false`
en el body.

---

## 💻 Ejemplos de Uso

### 🔹 Verificar estado de la impresora

```bash
curl -X GET "http://localhost:7249/api/Impresion/Estado/COM4"
```

### 🔹 Imprimir una factura (devuelve `numFiscal`)

```bash
curl -X POST "http://localhost:7249/api/Impresion/imprimirFact" \
  -H "Content-Type: application/json" \
  -d '{
    "cliente": {
      "cli_des": "EMPRESA EJEMPLO C.A.",
      "rif_cli": "J-12345678-9",
      "direc_cli": "Av. Principal, Edif. Central",
      "tlf_cli": "0212-5555555"
    },
    "items": [
      { "des_art": "Producto 1", "cantidad": 2, "precio": 10.50, "tipo_iva": 2, "porc_desc": "" }
    ],
    "forma_pagos": [
      { "des_fb": "Efectivo", "tot_bs": 21.00, "cod_fis": "01" }
    ],
    "igtf": false,
    "fact_num": "",
    "puerto": "COM4"
  }'
```

Respuesta:

```json
{ "status": true, "mensaje": "Factura impresa correctamente.", "numFiscal": "00001234" }
```

### 🔹 Imprimir una nota de crédito (devolución)

```bash
curl -X POST "http://localhost:7249/api/Impresion/ImprimirNcr" \
  -H "Content-Type: application/json" \
  -d '{
    "cabecera": {
      "des_cli": "EMPRESA EJEMPLO C.A.",
      "num_doc": "0000000123",
      "fec_emis": "2026-08-25",
      "puerto_fiscal": "COM4",
      "igtf": false
    },
    "detalles": [
      { "des_art": "Producto 1", "total_art": 1, "precio_bs": 10.50, "tipo_imp": 2 }
    ]
  }'
```

### 🔹 Realizar cierre Z

```bash
curl -X POST "http://localhost:7249/api/Impresion/ImprimirReporte/COM4" \
  -H "Content-Type: application/json" \
  -d '{"reporte": "Z"}'
```

### 🔹 Imprimir un ticket no fiscal (USB)

```bash
curl -X POST "http://localhost:7249/api/ImpresionTicket/Imprimir" \
  -H "Content-Type: application/json" \
  -d '{ "PrinterName": "EPSON TM-T20", "EscPosCode": "<base64-esc-pos>" }'
```

### 🔹 Imprimir un ticket no fiscal (red / IP)

```bash
curl -X POST "http://localhost:7249/api/ImpresionTicket/ImprimirIP" \
  -H "Content-Type: application/json" \
  -d '{ "PrinterIp": "192.168.1.50", "EscPosCode": "<base64-esc-pos>" }'
```

### 🔹 Info del equipo

```bash
curl -X GET "http://localhost:7249/api/ImpresionTicket/PcInfo"
```

### 🔹 En qué COM está la impresora fiscal

```bash
curl -X GET "http://localhost:7249/api/ImpresionTicket/PuertoFiscal"
```

Detectada (salida real con una HKA80 conectada):

```json
{
  "puerto": "COM5",
  "impresoraFiscal": "HKA80",
  "origen": "id-hardware",
  "detalle": "HKA80 reconocida por el VID/PID de su puente USB-serie (USB\\VID_04D8&PID_000A\\5&211D2413&0&1). Windows la muestra como \"Dispositivo serie USB (COM5)\".",
  "candidatos": [
    {
      "puerto": "COM5",
      "descripcion": "Dispositivo serie USB (COM5)",
      "fabricante": "Microsoft",
      "idHardware": "USB\\VID_04D8&PID_000A\\5&211D2413&0&1",
      "coincideFiscal": true,
      "motivoCoincidencia": "id-hardware",
      "modelo": "HKA80"
    }
  ]
}
```

Sin detectar — `puerto` en `null` y la lista para elegir:

```json
{
  "puerto": null,
  "impresoraFiscal": null,
  "origen": "ambiguo",
  "detalle": "Hay 2 puertos serie y ninguno se identifica como impresora fiscal. Si la fiscal esta conectada, su puente USB-serie no figura en PuertoFiscal:Modelos...",
  "candidatos": [ { "puerto": "COM3", "...": "..." }, { "puerto": "COM4", "...": "..." } ]
}
```

### 🔹 Cambiar la impresora predeterminada

```bash
curl -X PUT "http://localhost:7249/api/ImpresionTicket/ImpresoraPredeterminada" \
  -H "Content-Type: application/json" \
  -d '{"nombre":"AON PR-300"}'
```

```json
{ "exito": true, "mensaje": "\"AON PR-300\" quedo como predeterminada.", "predeterminada": "AON PR-300" }
```

Se **valida que la impresora exista** antes de tocar nada, y se **relee después de aplicar**: el
`exito` sale de comparar lo que realmente quedó, no de confiar en que la API de Windows no dio error.

```json
{ "exito": false, "mensaje": "Esta maquina no tiene una impresora llamada \"Impresora X\".", "predeterminada": "AON PR-300" }
```

---

## ⚠️ Notas Importantes

### 📌 Limitaciones
- **Descripciones**: Cliente máx. 40, Artículos máx. 37, Dirección máx. 40 caracteres.
- **Formatos Numéricos**: Cantidad 3 decimales; Precio y Montos 2 decimales.
- El **ESC/POS del ticket llega en Base64** ya armado; debe codificarse **byte a byte (Latin-1)** — un
  ticket con QR codificado en UTF-8 corrompe el byte de longitud del QR.

### 🔒 Consideraciones de Seguridad
- La API escucha en **todas las interfaces de red** en el puerto **7249** (`ListenAnyIP(7249)` en
  `Program.cs`), **no solo en `localhost`**. Es intencional: en el cobro, el front imprime contra la
  **IP de la estación remota**, no contra la máquina del cajero.
  > Consecuencia operativa: el **firewall de Windows** debe permitir entrada TCP en el **7249** en cada
  > caja. Si la impresión remota falla pero `localhost` funciona, el problema es el firewall.
- CORS configurado para permitir **cualquier origen**.
- No requiere autenticación (es un servicio local).

### 🖥️ Configuración de Windows Service
```csharp
// La API está configurada para ejecutarse como servicio Windows
builder.Host.UseWindowsService();
```

### 📊 Manejo de Conexiones (fiscal)
- La conexión se abre antes de cada operación y se cierra después.
- **La lectura del frame S1 ocurre con el puerto aún abierto**, tras finalizar el documento (si no, el
  correlativo todavía no incrementó).
- Puerto por defecto: COM4 (configurable en `appsettings.json`).

### 🔄 Flujo de Comunicación (fiscal)
1. **Conectar** → `OpenFpctrl(puerto)`
2. **Verificar estado** → `ReadFpStatus()`
3. **Enviar comandos** → `SendCmd(comando)`
4. **(Con IGTF)** → `SendCmd("199")` para finalizar el documento
5. **Leer correlativo** → `UploadStatusCmd(..., "S1", ...)` → `numFiscal`
6. **Cerrar conexión** → `CloseFpctrl()`

---

## 📚 Referencias Rápidas

### 🎯 Comandos HKA
| Comando | Descripción |
|---------|-------------|
| `iR*123456789` | Enviar RIF del cliente |
| `iS*EMPRESA` | Enviar razón social |
| `iF*02125555555` | Enviar teléfono |
| `iD*DIRECCION` | Enviar dirección |
| `iF:*00000123` | Número de factura afectada (NCR) |
| `iD:*27-07-2026` | Fecha factura afectada |
| `199` | Finalizar documento (modo flag 50, para IGTF) |
| `I0X` | Reporte X |
| `I0Z` | Reporte Z |
| `101` | Cierre de documento |
| `91` | Consultar último cierre Z |

### 📋 Códigos de Estado (S2)
| Código | Descripción |
|--------|-------------|
| 0 | Sin documento abierto |
| 1 | Factura abierta |
| 3 | Nota de Crédito abierta |
| 4 | Nota de Débito abierta |

---
