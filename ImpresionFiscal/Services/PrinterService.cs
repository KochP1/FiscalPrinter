using ImpresionFiscal.Controllers;
using RawPrint.NetStd;
using System.IO.Ports;
using System.Management;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using static ImpresionFiscal.Dtos.PuertoFiscal;
using static ImpresionFiscal.Dtos.Ticket;

namespace ImpresionFiscal.Services
{
    public class PrinterService
    {
        private readonly ILogger<ImpresionTicketController> _logger;
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;

        // Los modelos de fiscal que se saben reconocer, y con que. Se sobreescriben por
        // instalacion con PuertoFiscal:Modelos en appsettings.json, sin recompilar: agregar un
        // modelo nuevo es agregar una entrada.
        //
        // El VID/PID manda sobre el texto porque identifica el cable, no como se llame el driver
        // que le toco. Medido sobre una HKA80 conectada (2026-09-10): Windows la muestra como
        // "Dispositivo serie USB (COM5)", fabricante "Microsoft", y su DeviceID es
        // USB\VID_04D8&PID_000A\5&211D2413&0&1 (VID_04D8 = Microchip, PID_000A = su CDC de
        // emulacion RS-232). Las firmas de texto quedan como red para un driver propio.
        //
        // VID_04D8&PID_000A no es exclusivo de HKA: cualquier equipo con firmware CDC de
        // Microchip sin personalizar lo comparte. Si en una caja aparece otro, la deteccion pasa
        // a "ambiguo" y ofrece elegir — no elige mal en silencio.
        private static readonly List<ModeloFiscalConfig> ModelosPorDefecto = new()
        {
            new ModeloFiscalConfig
            {
                Nombre = "HKA80",
                IdsHardware = new List<string> { "VID_04D8&PID_000A" },
                Firmas = new List<string> { "hka", "tfhka", "the factory" }
            }
        };

        public PrinterService(ILogger<ImpresionTicketController> logger, IHostEnvironment environment, IConfiguration configuration)
        {
            _logger = logger;
            _environment = environment;
            _configuration = configuration;
        }

        public bool IsPrinterOnline(string printerIp)
        {
            try
            {
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = ping.Send(printerIp, 1000);
                    return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task SendBytesToPrinterTcp(string printerIp, byte[] escPosBytes)
        {
            try
            {
                using (var cliente = new TcpClient())
                {
                    // Timeout de conexion/envio de 5s (paridad con el setTimeout(5000) del servicio Node original).
                    // Evita que la conexion quede bloqueada ~21s si el host descarta el SYN silenciosamente.
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await cliente.ConnectAsync(printerIp, 9100, cts.Token);
                        using (var stream = cliente.GetStream())
                        {
                            await stream.WriteAsync(escPosBytes, 0, escPosBytes.Length, cts.Token);
                            await stream.FlushAsync(cts.Token);
                            _logger.LogInformation($"Datos enviados a {printerIp}:9100");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError($"Timeout (5s) al conectar/enviar a la impresora {printerIp}:9100");
                throw new TimeoutException($"Timeout (5s) al conectar o enviar a la impresora {printerIp}:9100");
            }
            catch (Exception ex)
            {
                // No tragamos la excepcion: la propagamos para que el controlador exponga el error real.
                _logger.LogError($"Error al enviar a impresora TCP {printerIp}:9100: {ex.Message}");
                throw;
            }
        }

        public void SendBytesToPrinterUsb(string printerName, byte[] escPosBytes)
        {
            try
            {
                using (var stream = new MemoryStream(escPosBytes))
                {
                    var printer = new Printer();
                    printer.PrintRawStream(printerName, stream, "ESC/POS Ticket", false);

                    _logger.LogInformation($"Impresión enviada a {printerName}");
                }
            }
            catch (Exception ex)
            {
                // No tragamos la excepcion: la propagamos para que el controlador exponga el error real del spooler.
                _logger.LogError($"Error al enviar a impresora USB '{printerName}': {ex.Message}");
                throw;
            }
        }

        public Dictionary<string, List<string>> ObtenerInformacionRed()
        {
            var resultado = new Dictionary<string, List<string>>();

            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Omitir interfaces no IPv4 y loopback
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                        ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;

                    var props = ni.GetIPProperties();
                    foreach (var ip in props.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            if (!resultado.ContainsKey(ni.Name))
                                resultado[ni.Name] = new List<string>();

                            resultado[ni.Name].Add(ip.Address.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener información de red: {ex.Message}");
            }

            return resultado;
        }

        public List<ImpresoraInfo> ObtenerImpresorasWindows()
        {
            var impresoras = new List<ImpresoraInfo>();

            // Idem: la predeterminada la dice Windows, no la columna `Default` de WMI.
            var nombrePredeterminada = NombreImpresoraPredeterminada();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        var nombreImpresora = printer["Name"]?.ToString() ?? "N/A";

                        impresoras.Add(new ImpresoraInfo
                        {
                            Name = nombreImpresora,
                            DriverName = printer["DriverName"]?.ToString() ?? "N/A",
                            PortName = printer["PortName"]?.ToString() ?? "N/A",
                            Default = string.Equals(nombreImpresora.Trim(), nombrePredeterminada?.Trim(),
                                                    StringComparison.OrdinalIgnoreCase),
                            Shared = printer["Shared"] != null && (bool)printer["Shared"],
                            Location = printer["Location"]?.ToString() ?? "N/A",
                            Comment = printer["Comment"]?.ToString() ?? "N/A"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener impresoras: {ex.Message}");
            }

            return impresoras;
        }

        // API de Windows para fijar la impresora predeterminada. Se usa esta y no
        // `rundll32 printui.dll` para no lanzar un proceso hijo.
        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetDefaultPrinter(string nombre);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetDefaultPrinter(System.Text.StringBuilder buffer, ref int tamano);

        /// <summary>
        /// El nombre de la impresora predeterminada, leido por la API de Windows.
        ///
        /// NO se usa WMI acá, y esto se midió: `Win32_Printer.Default` queda PEGADO. Cambiando la
        /// predeterminada a "Microsoft Print to PDF" (2026-09-10), HKCU\...\Windows\Device ya decía
        /// "Microsoft Print to PDF" y WMI seguía informando la anterior, incluso desde un proceso
        /// nuevo. `GetDefaultPrinter` es del mismo subsistema que la escribe, así que no miente.
        /// </summary>
        private string NombreImpresoraPredeterminada()
        {
            try
            {
                int tamano = 0;

                // Primera llamada con buffer vacio: Windows devuelve en `tamano` cuanto necesita.
                GetDefaultPrinter(null, ref tamano);

                if (tamano <= 0) { return null; }

                var buffer = new System.Text.StringBuilder(tamano);

                if (GetDefaultPrinter(buffer, ref tamano))
                {
                    var nombre = buffer.ToString().Trim();

                    return string.IsNullOrEmpty(nombre) ? null : nombre;
                }

                _logger.LogError($"GetDefaultPrinter fallo. Codigo Win32: {Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error leyendo la impresora predeterminada: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Cambia la impresora predeterminada de esta maquina.
        ///
        /// La predeterminada de Windows es POR USUARIO (vive en
        /// HKCU), asi que esto cambia la del usuario bajo el que corre este servicio. Es
        /// exactamente el mismo contexto que lee `ObtenerImpresoraPredeterminadaWindows` y que usa
        /// `ImprimirAuto` para resolver a donde imprimir, o sea que la palanca es la correcta para
        /// lo que importa: a donde salen los tickets.
        ///
        /// Lo que NO garantiza: que coincida con lo que el cajero ve en "Impresoras y escaneres"
        /// de su sesion, si el servicio corre bajo otra cuenta.
        /// </summary>
        public async Task<ImpresoraPredeterminadaRes> EstablecerImpresoraPredeterminada(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return new ImpresoraPredeterminadaRes
                {
                    Exito = false,
                    Mensaje = "No se indico el nombre de la impresora.",
                    Predeterminada = NombreImpresoraPredeterminada()
                };
            }

            nombre = nombre.Trim();

            // Se valida contra las impresoras reales antes de tocar nada: SetDefaultPrinter con un
            // nombre que no existe puede dejar la configuracion apuntando a la nada.
            var instaladas = ObtenerImpresorasWindows();
            var elegida = instaladas.FirstOrDefault(i =>
                string.Equals(i.Name?.Trim(), nombre, StringComparison.OrdinalIgnoreCase));

            if (elegida == null)
            {
                return new ImpresoraPredeterminadaRes
                {
                    Exito = false,
                    Mensaje = $"Esta maquina no tiene una impresora llamada \"{nombre}\".",
                    Predeterminada = NombreImpresoraPredeterminada()
                };
            }

            bool aplicado = await Task.Run(() => SetDefaultPrinter(elegida.Name));

            if (!aplicado)
            {
                int codigo = Marshal.GetLastWin32Error();
                _logger.LogError($"SetDefaultPrinter fallo para '{elegida.Name}'. Codigo Win32: {codigo}");

                return new ImpresoraPredeterminadaRes
                {
                    Exito = false,
                    Mensaje = $"Windows rechazo el cambio (codigo {codigo}).",
                    Predeterminada = NombreImpresoraPredeterminada()
                };
            }

            // Se relee: es la unica prueba de que quedo. Un true de la API sin confirmar deja
            // pasar el caso en que el cambio no sobrevive al contexto del servicio.
            var quedo = NombreImpresoraPredeterminada();
            bool coincide = string.Equals(quedo?.Trim(), elegida.Name?.Trim(), StringComparison.OrdinalIgnoreCase);

            return new ImpresoraPredeterminadaRes
            {
                Exito = coincide,
                Mensaje = coincide
                    ? $"\"{elegida.Name}\" quedo como predeterminada."
                    : $"Se aplico el cambio pero Windows sigue informando \"{quedo}\" como predeterminada.",
                Predeterminada = quedo
            };
        }

        public async Task<List<dynamic>> ObtenerImpresoraPredeterminadaWindows()
        {
            var resultado = new List<dynamic>();

            // El nombre sale de GetDefaultPrinter, NO del `Default=True` de WMI, que queda pegado
            // (ver NombreImpresoraPredeterminada). Con el filtro viejo, cambiar la predeterminada
            // en Windows no movia a donde ImprimirAuto manda los tickets.
            var nombrePredeterminada = NombreImpresoraPredeterminada();

            if (string.IsNullOrEmpty(nombrePredeterminada))
            {
                _logger.LogError("No se pudo determinar la impresora predeterminada del equipo.");
                return resultado;
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_Printer WHERE Name = '" + nombrePredeterminada.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        var nombre = printer["Name"]?.ToString() ?? "N/A";
                        var driverName = printer["DriverName"]?.ToString() ?? "N/A";
                        var portName = printer["PortName"]?.ToString() ?? "N/A";
                        var printerStatus = printer["PrinterStatus"] != null ? Convert.ToInt32(printer["PrinterStatus"]) : 0;
                        var shared = printer["Shared"] != null && (bool)printer["Shared"];
                        var ubicacion = printer["Location"]?.ToString() ?? "N/A";
                        var comentario = printer["Comment"]?.ToString() ?? "N/A";

                        // Extraer IP del PortName
                        string ip = "N/A";
                        if (!string.IsNullOrEmpty(portName))
                        {
                            var ipMatch = System.Text.RegularExpressions.Regex.Match(portName, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                            if (ipMatch.Success)
                                ip = ipMatch.Groups[1].Value;
                        }

                        // Determinar lenguaje basado en DriverName
                        string lenguaje = DeterminarLenguaje(nombre, driverName);

                        // Determinar tipo
                        string tipo = "Virtual";
                        if (ip != "N/A") tipo = "Red";
                        else if (!string.IsNullOrEmpty(portName) &&
                                (portName.StartsWith("COM") || portName.StartsWith("LPT") || portName.StartsWith("USB")))
                            tipo = "Local";
                        else if (!string.IsNullOrEmpty(portName) && portName.Contains("WSD"))
                            tipo = "Web Services";

                        // Estado traducido
                        string estado = "Desconocido";
                        switch (printerStatus)
                        {
                            case 3: estado = "Inactivo"; break;
                            case 4: estado = "Imprimiendo"; break;
                            case 9: estado = "Listo"; break;
                        }

                        resultado.Add(new
                        {
                            nombre = nombre,
                            driver = driverName,
                            lenguaje = lenguaje,
                            estado = estado,
                            trabajosEnCola = 0,
                            compartida = shared,
                            tipo = tipo,
                            tipoDetalle = "N/A",
                            ip = ip,
                            puerto = portName,
                            ubicacion = ubicacion,
                            comentario = comentario,
                            fechaInstalacion = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            capacidades = ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener impresora predeterminada: {ex.Message}");
            }

            return resultado;
        }

        // Enumera los puertos serie de ESTA maquina con lo que Windows sabe de cada uno.
        //
        // Se cruzan dos fuentes a proposito. Win32_PnPEntity trae la descripcion y el fabricante
        // (lo unico con lo que se puede reconocer una fiscal), pero un puerto sin entrada PnP no
        // aparece; SerialPort.GetPortNames() los lista todos pero sin ningun dato. La union deja
        // que no se pierda ninguno y que los que se puedan describir vengan descritos.
        public List<PuertoSerieInfo> ObtenerPuertosSerie()
        {
            var puertos = new Dictionary<string, PuertoSerieInfo>(StringComparer.OrdinalIgnoreCase);
            var modelos = ObtenerModelosFiscales();

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Caption, Name, Manufacturer, DeviceID FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
                {
                    foreach (ManagementObject dispositivo in searcher.Get())
                    {
                        var caption = dispositivo["Caption"]?.ToString() ?? string.Empty;
                        var nombre = ExtraerNombreDeCom(caption);

                        if (string.IsNullOrEmpty(nombre)) { continue; }

                        var fabricante = dispositivo["Manufacturer"]?.ToString() ?? "N/A";
                        var idHardware = dispositivo["DeviceID"]?.ToString() ?? "N/A";

                        var (modelo, motivo) = IdentificarModelo(modelos, idHardware, caption, fabricante);

                        puertos[nombre] = new PuertoSerieInfo
                        {
                            Puerto = nombre,
                            Descripcion = caption,
                            Fabricante = fabricante,
                            IdHardware = idHardware,
                            CoincideFiscal = motivo != null,
                            MotivoCoincidencia = motivo,
                            Modelo = modelo
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // No se corta: abajo quedan al menos los nombres de SerialPort.
                _logger.LogError($"Error consultando los puertos serie por WMI: {ex.Message}");
            }

            try
            {
                foreach (var nombre in SerialPort.GetPortNames())
                {
                    if (puertos.ContainsKey(nombre)) { continue; }

                    puertos[nombre] = new PuertoSerieInfo
                    {
                        Puerto = nombre,
                        Descripcion = "N/A",
                        Fabricante = "N/A",
                        IdHardware = "N/A",
                        CoincideFiscal = false,
                        MotivoCoincidencia = null,
                        Modelo = null
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error enumerando los puertos serie: {ex.Message}");
            }

            return puertos.Values.OrderBy(p => p.Puerto, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Deduce en que COM esta la impresora fiscal, SIN abrir ningun puerto.
        //
        // Por que no se prueba puerto por puerto: la unica forma de confirmarlo con certeza es
        // OpenFpctrl contra cada candidato, y esa DLL tiene una sola conexion global. Sondear
        // mientras la caja esta facturando puede robarle el puerto a un documento fiscal en vuelo.
        // Un documento fiscal a medias es peor que un campo que hay que llenar a mano.
        //
        // Por eso `Puerto` viene con valor solo cuando el resultado es concluyente; si no, devuelve
        // los candidatos y que decida quien configura la estacion.
        // `Candidatos` NO es la lista de puertos de la maquina: es lo que queda por decidir.
        // Cuando la deteccion es concluyente trae un solo elemento, el de la fiscal. Solo cuando
        // no se puede decidir trae varios, porque ahi la lista ES la respuesta que se necesita.
        public PuertoFiscalInfo DetectarPuertoFiscal()
        {
            var puertos = ObtenerPuertosSerie();
            var info = new PuertoFiscalInfo();

            if (puertos.Count == 0)
            {
                info.Origen = "sin-puertos";
                info.Detalle = "Esta maquina no tiene puertos serie visibles para Windows.";
                return info;
            }

            var reconocidos = puertos.Where(p => p.CoincideFiscal).ToList();

            if (reconocidos.Count == 1)
            {
                var elegido = reconocidos[0];

                info.Puerto = elegido.Puerto;
                info.ImpresoraFiscal = elegido.Modelo;
                info.Origen = elegido.MotivoCoincidencia;
                info.Detalle = elegido.MotivoCoincidencia == "id-hardware"
                    ? $"{elegido.Modelo} reconocida por el VID/PID de su puente USB-serie " +
                      $"({elegido.IdHardware}). Windows la muestra como \"{elegido.Descripcion}\"."
                    : $"{elegido.Modelo} reconocida por su descripcion: \"{elegido.Descripcion}\".";
                info.Candidatos = reconocidos;
                return info;
            }

            if (reconocidos.Count > 1)
            {
                // Varios coinciden: no se elige uno, pero se descartan los que no coincidieron.
                info.Origen = "ambiguo";
                info.Detalle = $"Hay {reconocidos.Count} puertos cuya descripcion coincide con una impresora fiscal. " +
                               "Elegi cual corresponde a esta caja.";
                info.Candidatos = reconocidos;
                return info;
            }

            if (puertos.Count == 1)
            {
                info.Puerto = puertos[0].Puerto;
                info.Origen = "unico-puerto";
                info.Detalle = $"Ninguna descripcion coincide con una fiscal, pero {puertos[0].Puerto} es el unico " +
                               "puerto serie de la maquina. Verificalo antes de guardarlo.";
                info.Candidatos = puertos;
                return info;
            }

            // Nada reconocible y varios puertos: aca si van todos, porque es lo unico que se puede
            // ofrecer para que alguien elija a mano.
            info.Origen = "ambiguo";
            info.Detalle = $"Hay {puertos.Count} puertos serie y ninguno se identifica como impresora fiscal. " +
                           "Si la fiscal esta conectada, su puente USB-serie no figura en " +
                           "PuertoFiscal:Modelos: fijate el idHardware del puerto que le corresponde y agregalo " +
                           "al modelo que sea. Mientras tanto, elegi el puerto a mano.";
            info.Candidatos = puertos;
            return info;
        }

        // Los modelos configurados, o los de por defecto si la instalacion no define ninguno.
        private List<ModeloFiscalConfig> ObtenerModelosFiscales()
        {
            try
            {
                var configurados = _configuration.GetSection("PuertoFiscal:Modelos").Get<List<ModeloFiscalConfig>>();

                if (configurados != null && configurados.Count > 0)
                {
                    return configurados.Where(m => !string.IsNullOrWhiteSpace(m?.Nombre)).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error leyendo PuertoFiscal:Modelos; se usan los de por defecto: {ex.Message}");
            }

            return ModelosPorDefecto;
        }

        // Devuelve (modelo, motivo) del primer modelo que reconozca este puerto, o (null, null).
        //
        // Se recorren TODOS los modelos por VID/PID antes de probar las firmas de texto: un
        // acierto por hardware vale mas que un acierto por nombre de driver, aunque el modelo del
        // hardware venga mas abajo en la lista.
        private static (string modelo, string motivo) IdentificarModelo(
            List<ModeloFiscalConfig> modelos, string idHardware, string descripcion, string fabricante)
        {
            foreach (var modelo in modelos)
            {
                if (CoincideAlgunId(modelo.IdsHardware, idHardware))
                {
                    return (modelo.Nombre, "id-hardware");
                }
            }

            var texto = $"{descripcion} {fabricante}";

            foreach (var modelo in modelos)
            {
                if (modelo.Firmas != null &&
                    modelo.Firmas.Any(f => !string.IsNullOrWhiteSpace(f) &&
                                           texto.Contains(f, StringComparison.OrdinalIgnoreCase)))
                {
                    return (modelo.Nombre, "firma");
                }
            }

            return (null, null);
        }

        // El DeviceID de Windows viene como "USB\VID_04D8&PID_000A\5&211D2413&0&1": el ultimo tramo
        // es la instancia y cambia de puerto a puerto, asi que se busca por contenido y no por
        // igualdad. Alcanza con configurar "VID_04D8&PID_000A".
        private static bool CoincideAlgunId(List<string> idsHardware, string idHardware)
        {
            if (idsHardware == null) { return false; }
            if (string.IsNullOrWhiteSpace(idHardware) || idHardware == "N/A") { return false; }

            return idsHardware.Any(i => !string.IsNullOrWhiteSpace(i) &&
                                        idHardware.Contains(i, StringComparison.OrdinalIgnoreCase));
        }

        // "The Factory HKA (COM4)" -> "COM4". Se toma la ULTIMA aparicion porque el nombre del
        // dispositivo puede contener parentesis propios antes del que trae el puerto.
        private static string ExtraerNombreDeCom(string caption)
        {
            var coincidencias = System.Text.RegularExpressions.Regex.Matches(caption, @"\((COM\d+)\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return coincidencias.Count > 0
                ? coincidencias[coincidencias.Count - 1].Groups[1].Value.ToUpperInvariant()
                : string.Empty;
        }

        public string DeterminarLenguaje(string nombre, string driverName)
        {
            if (string.IsNullOrEmpty(driverName))
                return "N/A";

            var driverUpper = driverName.ToUpper();
            var nombreUpper = nombre?.ToUpper() ?? "";

            // Impresoras térmicas/POS
            if (driverUpper.Contains("POS") || driverUpper.Contains("RECEIPT") ||
                driverUpper.Contains("THERMAL") || driverUpper.Contains("TICKET"))
                return "ESC/POS";

            // Lenguajes de impresoras de red/industrial
            if (driverUpper.Contains("ZPL") || driverUpper.Contains("ZEBRA")) return "ZPL (Zebra)";
            if (driverUpper.Contains("EPL") || driverUpper.Contains("ELTRON")) return "EPL (Eltron)";
            if (driverUpper.Contains("CPCL")) return "CPCL (Comtec)";
            if (driverUpper.Contains("DPL")) return "DPL (Datamax)";
            if (driverUpper.Contains("IPL")) return "IPL (Intermec)";
            if (driverUpper.Contains("KPDL") || driverUpper.Contains("KYOCERA")) return "KPDL (Kyocera)";
            if (driverUpper.Contains("LCDS") || driverUpper.Contains("LEXMARK")) return "LCDS (Lexmark)";
            if (driverUpper.Contains("SPL") || driverUpper.Contains("SAMSUNG")) return "SPL (Samsung)";

            // Lenguajes estándar
            if (System.Text.RegularExpressions.Regex.IsMatch(driverUpper, @"PCL\d?")) return "PCL (HP)";
            if (driverUpper.Contains("POSTSCRIPT") || driverUpper.Contains("PS ")) return "PostScript (Adobe)";
            if (driverUpper.Contains("XPS")) return "XPS (Microsoft)";
            if (driverUpper.Contains("PDF")) return "PDF";
            if (driverUpper.Contains("ONENOTE")) return "OneNote";
            if (driverUpper.Contains("GDI")) return "GDI (Windows)";

            // Por nombre de impresora también
            if (nombreUpper.Contains("POS") || nombreUpper.Contains("TERMICA") ||
                nombreUpper.Contains("TICKET") || nombreUpper.Contains("RECIBO"))
                return "ESC/POS";

            return "N/A";
        }
    }
}
