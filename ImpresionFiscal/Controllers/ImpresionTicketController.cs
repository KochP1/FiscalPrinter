using ImpresionFiscal.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;
using static ImpresionFiscal.Dtos.Ticket;

namespace ImpresionFiscal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImpresionTicketController : ControllerBase
    {
        private readonly ILogger<ImpresionTicketController> _logger;
        private readonly PrinterService _printerService;

        public ImpresionTicketController(ILogger<ImpresionTicketController> logger, PrinterService printerService)
        {
            _logger = logger;
            _printerService = printerService;
        }

        #region GET ENDPOINTS

        [HttpGet("printers")]
        public IActionResult GetPrinters()
        {
            try
            {
                var printers = _printerService.ObtenerImpresorasWindows();
                return Ok(printers);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener impresoras: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("PcInfo")]
        public async Task<IActionResult> GetPcInfo()
        {
            try
            {
                var hostname = Environment.MachineName;
                var impresoras = await _printerService.ObtenerImpresoraPredeterminadaWindows();
                var puertoFiscal = _printerService.DetectarPuertoFiscal();

                // Diccionario para preservar EXACTAMENTE las claves (Equipo, IpResultante, impresoras)
                // que espera el front. El serializador aplica camelCase a propiedades, pero respeta las
                // claves de diccionario, igual que el servicio Node original ("Equipo"/"IpResultante").
                var resultado = new Dictionary<string, object>
                {
                    ["Equipo"] = hostname,
                    ["IpResultante"] = _printerService.ObtenerInformacionRed(),
                    ["impresoras"] = impresoras,
                    // El COM de la fiscal y su modelo cuando se pudieron deducir; null cuando no.
                    // Claves nuevas: no rompen a ningun consumidor de PcInfo que ya exista.
                    ["PuertoFiscal"] = puertoFiscal.Puerto,
                    ["ImpresoraFiscal"] = puertoFiscal.ImpresoraFiscal,
                    ["PuertoFiscalDetalle"] = puertoFiscal
                };

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener información del PC: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Cambia la impresora predeterminada de esta máquina, para no tener que ir a
        // "Impresoras y escáneres" de Windows.
        //
        // Es la misma predeterminada que resuelve `ImprimirAuto`, así que esto decide a dónde
        // salen los tickets. Ver PrinterService.EstablecerImpresoraPredeterminada por la
        // advertencia del contexto de usuario.
        [HttpPut("ImpresoraPredeterminada")]
        public async Task<IActionResult> SetImpresoraPredeterminada([FromBody] ImpresoraPredeterminadaRequest request)
        {
            try
            {
                var res = await _printerService.EstablecerImpresoraPredeterminada(request?.Nombre);

                // 200 con Exito=false y no un 4xx: el front necesita el mensaje y la
                // predeterminada que quedó para poder mostrar el estado real.
                return Ok(res);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al cambiar la impresora predeterminada: {ex.Message}");
                return StatusCode(500, new { exito = false, mensaje = ex.Message });
            }
        }

        // Devuelve SOLO el puerto COM de la impresora fiscal y los candidatos, sin traerse el
        // PcInfo entero. Pensado para la pantalla de configuración de estaciones: llena el campo
        // "Puerto COM" con lo que hay en la máquina en vez de que alguien lo tipee de memoria.
        //
        // No abre ningún puerto: la deducción es por descripción del dispositivo. Cuando no puede
        // decidir, `puerto` viene en null y `candidatos` trae la lista para elegir. Ver
        // PrinterService.DetectarPuertoFiscal.
        [HttpGet("PuertoFiscal")]
        public IActionResult GetPuertoFiscal()
        {
            try
            {
                return Ok(_printerService.DetectarPuertoFiscal());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al detectar el puerto de la impresora fiscal: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Devuelve SOLO el nombre del equipo (hostname). El POS lo usa para armar el cobro y validar
        // la estación, sin tener que pedir todo el PcInfo (impresoras, red, etc.).
        [HttpGet("Equipo")]
        public IActionResult GetEquipo()
        {
            try
            {
                return Ok(new { equipo = Environment.MachineName });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener el nombre del equipo: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        #endregion

        #region POST ENDPOINTS
        [HttpPost("ImprimirIP")]
        public async Task<IActionResult> ImprimirPorIP([FromBody] ImpresionIPRequest request)
        {
            _logger.LogInformation("Solicitud de impresión por IP recibida");

            try
            {
                // Validar request
                if (string.IsNullOrEmpty(request.PrinterIp) || string.IsNullOrEmpty(request.EscPosCode))
                {
                    return BadRequest(new { success = false, error = "printerIp y escPosCode son requeridos" });
                }

                byte[] buffer;
                try
                {
                    buffer = Convert.FromBase64String(request.EscPosCode);
                    _logger.LogInformation($"Bytes decodificados: {buffer.Length}");
                }
                catch (FormatException)
                {
                    return BadRequest(new { success = false, error = "El código ESC/POS no es un Base64 válido" });
                }

                // Sin gate de ping: conectamos directo como el servicio Node original (muchas impresoras
                // bloquean ICMP pero aceptan TCP 9100). El timeout de 5s evita que quede colgado.
                await _printerService.SendBytesToPrinterTcp(request.PrinterIp, buffer);

                return Ok(new { success = true, message = "Impresión enviada correctamente vía TCP/IP" });
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex.Message);
                return StatusCode(408, new { success = false, error = ex.Message });
            }
            catch (SocketException ex)
            {
                _logger.LogError($"Error de conexión TCP: {ex.Message}");
                return StatusCode(500, new { success = false, error = $"No se pudo conectar a la impresora IP: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error inesperado: {ex.Message}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost("Imprimir")]
        public IActionResult Imprimir([FromBody] ImpresionRequest request)
        {
            _logger.LogInformation("Solicitud de impresión recibida");

            try
            {
                // 1. Validación básica
                if (string.IsNullOrEmpty(request.PrinterName) || string.IsNullOrEmpty(request.EscPosCode))
                {
                    return BadRequest(new { success = false, error = "printerName y escPosCode son requeridos" });
                }

                byte[] buffer;
                try
                {
                    buffer = Convert.FromBase64String(request.EscPosCode);
                    _logger.LogInformation($"Impresora: {request.PrinterName}, Bytes: {buffer.Length}");
                }
                catch (FormatException)
                {
                    return BadRequest(new { success = false, error = "El código ESC/POS no es un Base64 válido" });
                }

                _printerService.SendBytesToPrinterUsb(request.PrinterName, buffer);

                return Ok(new
                {
                    success = true,
                    message = "Ticket enviado a la cola de impresión",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al imprimir: {ex.Message}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        // El front manda solo el ESC/POS; aca resolvemos la impresora predeterminada de ESTA maquina
        // y decidimos IP (TCP 9100) vs USB. Asi el front no necesita consumir PcInfo para imprimir.
        [HttpPost("ImprimirAuto")]
        public async Task<IActionResult> ImprimirAuto([FromBody] ImpresionAutoRequest request)
        {
            _logger.LogInformation("Solicitud de impresión automática recibida");

            try
            {
                if (string.IsNullOrEmpty(request.EscPosCode))
                {
                    return BadRequest(new { success = false, error = "escPosCode es requerido" });
                }

                byte[] buffer;
                try
                {
                    buffer = Convert.FromBase64String(request.EscPosCode);
                }
                catch (FormatException)
                {
                    return BadRequest(new { success = false, error = "El código ESC/POS no es un Base64 válido" });
                }

                // Resolver la impresora predeterminada de Windows (misma fuente que PcInfo).
                var predeterminada = (await _printerService.ObtenerImpresoraPredeterminadaWindows()).FirstOrDefault();
                if (predeterminada == null)
                {
                    return StatusCode(500, new { success = false, error = "No se encontró una impresora predeterminada en el equipo" });
                }

                string nombre = predeterminada.nombre;
                string ip = predeterminada.ip;

                // Misma regla que usaba el front: si hay IP valida -> red (TCP 9100), si no -> USB por nombre.
                bool tieneIp = !string.IsNullOrWhiteSpace(ip) && ip.Trim() != "N/A";
                if (tieneIp)
                {
                    await _printerService.SendBytesToPrinterTcp(ip.Trim(), buffer);
                    return Ok(new { success = true, message = "Impresión enviada correctamente vía TCP/IP", printer = nombre, ip });
                }

                if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim() == "N/A")
                {
                    return StatusCode(500, new { success = false, error = "La impresora predeterminada no tiene IP ni nombre válidos" });
                }

                _printerService.SendBytesToPrinterUsb(nombre, buffer);
                return Ok(new { success = true, message = "Ticket enviado a la cola de impresión", printer = nombre });
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex.Message);
                return StatusCode(408, new { success = false, error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al imprimir (auto): {ex.Message}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        #endregion
    }
}
