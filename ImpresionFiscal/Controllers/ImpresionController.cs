using ImpresionFiscal.Dtos;
using Microsoft.AspNetCore.Mvc;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using static ImpresionFiscal.Dtos.FacturaDtos;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace ImpresionFiscalApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImpresionController : ControllerBase
    {

        #region DLL IMPORTS

        [DllImport("DLLs/tfhkaif.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int OpenFpctrl(string puerto);

        [DllImport("DLLs/tfhkaif.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern bool CloseFpctrl();

        [DllImport("DLLs/tfhkaif.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int ReadFpStatus(ref int status, ref int error);

        [DllImport("DLLs/tfhkaif.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int SendCmd(ref int status, ref int error, string cmd);

        // Vuelca un frame de estado (ej. "S1") a un archivo. Usado para leer el correlativo fiscal.
        [DllImport("DLLs/tfhkaif.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int UploadStatusCmd(ref int status, ref int error, string cmd, string fileCmd);

        #endregion

        #region GET ENDPOINTS
        [HttpGet]
        [Route("Ping")]
        public async Task<IActionResult> Ping()
        {
            int? puerto = HttpContext.Connection.LocalPort;
            return Ok(new
            {
                status = true,
                mensaje = $"Api ejecutandose en el puerto: {puerto}"
            });
        }

        [HttpGet]
        [Route("Estado/{puerto}")]
        public async Task<IActionResult> Estado(string puerto)
        {
            try
            {
                string res = await ObtenerEstado(puerto);

                return Ok(new
                {
                    status = res
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500,
                    $"Error interno obteniendo estado de la impresora, puerto {puerto}. {ex.InnerException?.Message ?? ex.Message}");
            }
            finally
            {
                try
                {
                    CloseFpctrl();
                }
                catch
                {
                }
            }
        }


        [HttpGet]
        [Route("checkDll")]
        public async Task<IActionResult> CheckDll()
        {
            try
            {
                var res = await ObtenerInfoDLL();
                return Ok(res);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno: {ex.InnerException.Message ?? ex.Message}");
            }
        }

        [HttpGet]
        [Route("EstadoDetallado/{puerto}")]
        public async Task<IActionResult> EstadoDetallado(string puerto)
        {
            bool conectado = false;
            string mensaje = "";

            try
            {
                conectado = await ConectarImp(puerto);

                int status = 0;
                int error = 0;

                int resultado = await Task.Run(() => ReadFpStatus(ref status, ref error));

                if (resultado == 1)
                {
                    mensaje = GetStatusError(status, error);
                }
                else
                {
                    conectado = false;
                    mensaje = $"Error de comunicación con la DLL al intentar leer el estado. Código devuelto: {resultado}.";
                }
            }
            catch (Exception ex)
            {
                conectado = false;
                mensaje = $"Excepción al obtener estado: {ex.InnerException?.Message ?? ex.Message}";
            }
            finally
            {
                try
                {
                    CloseFpctrl();
                }
                catch
                {
                    // Ignorar errores al cerrar
                }
            }

            return Ok(new
            {
                status = conectado,
                mensaje = mensaje
            });
        }
        #endregion

        #region POST METHODS
        [HttpPost]
        [Route("conectar/{puerto}")]
        public async Task<IActionResult> Conectar(string puerto)
        {
            try
            {
                await ConectarImp(puerto);

                return Ok(new
                {
                    status = true,
                    mensaje = $"Conectado al puerto: {puerto}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error conectándose al puerto {puerto}: {ex.InnerException?.Message ?? ex.Message}");
            }
            finally
            {
                try
                {
                    CloseFpctrl();
                }
                catch
                {
                    // Ignorar errores al cerrar la conexión
                }
            }
        }


        [HttpPost]
        [Route("imprimirFact")]
        public async Task<IActionResult> ImprimirFact([FromBody] FacturaFiscal factura)
        {
            try
            {
                // Recuperación defensiva: cerrar por si un run anterior dejó el puerto tomado.
                try { CloseFpctrl(); } catch { }

                await ConectarImp(factura.puerto);
                await VerificarImpresoraLista();

                if (string.IsNullOrEmpty(factura.cliente.rif_cli)) throw new Exception($"La información de la factura esta incompleta, hace falta el rif del cliente");
                if (string.IsNullOrEmpty(factura.cliente.cli_des)) throw new Exception($"La información de la factura esta incompleta, hace falta la descripción del cliente");
                if (string.IsNullOrEmpty(factura.cliente.direc_cli)) throw new Exception($"La información de la factura esta incompleta, hace falta la dirección del cliente");
                if (!factura.items.Any()) throw new Exception($"La información de la factura esta incompleta, no tiene articulos a procesar");

                // --- ENVIAR DATOS DEL CLIENTE ---
                // 1. RIF del Cliente
                string rif = factura.cliente.rif_cli.Trim().ToUpper().Replace("-", "");
                if (!SendCmd($"iR*{rif}"))
                    throw new Exception("Error enviando RIF");

                // 2. Razón Social (Máx 40 caracteres)
                string razonSocial = factura.cliente.cli_des.Trim();
                if (razonSocial.Length > 40) razonSocial = razonSocial.Substring(0, 40);

                if (!SendCmd($"iS*{razonSocial}"))
                    throw new Exception("Error enviando Razón Social");

                int nLinea = 0;

                if (!string.IsNullOrEmpty(factura.cliente.direc_cli))
                {
                    string direccion = factura.cliente.direc_cli.Trim();

                    // Si la dirección sobrepasa los 40 caracteres, la enviamos en 2 líneas de comentario.
                    if (direccion.Length > 40)
                    {
                        string dirLinea1 = direccion.Substring(0, 40);
                        string dirLinea2 = direccion.Substring(40, Math.Min(40, direccion.Length - 40));

                        SendCmd($"i{nLinea++:00}DIRECCION: {dirLinea1}");
                        SendCmd($"i{nLinea++:00}{dirLinea2}");
                    }
                    else
                    {
                        SendCmd($"i{nLinea++:00}DIRECCION: {direccion}");
                    }
                }

                // --- FACTURA DE SISTEMA / FACTSYS ---
                if (!string.IsNullOrEmpty(factura.fact_num?.Trim()))
                {
                    string numFactSistema = factura.fact_num.Trim();
                    if (numFactSistema.Length > 20) numFactSistema = numFactSistema.Substring(0, 20);

                    // Intentar comando nativo iF:* (Factura Afectada / Factura Sistema)
                    SendCmd($"i{nLinea++:00}Fac.Sis.: {numFactSistema}");
                    //if (!SendCmd($"iF:*{numFactSistema}"))
                    //{
                    //    // Fallback: Si no soporta iF:*, lo imprime como comentario i01 etiquetado exactamente como la foto
                    //    SendCmd($"i01Fac.Sis.: {numFactSistema}");
                    //}
                }

                // --- REGISTRAR LOS ARTÍCULOS ---
                foreach (var item in factura.items)
                {
                    string caracterIva = item.tipo_iva switch
                    {
                        1 => "!",
                        2 => "\"",
                        3 => "#",
                        _ => " "
                    };
                    string descrip = $"{item.co_art.Trim()}-{item.des_art.Trim()}";

                    // 1. Formatear la cantidad a 10 dígitos (en milésimas, x 1000)
                    // Opción A: Si la impresora requiere 3 decimales
                    long cantidadMilensimas = (long)Math.Round((decimal)item.cantidad * 1000m, MidpointRounding.AwayFromZero);
                    string strCantidad = cantidadMilensimas.ToString("00000000"); // 8 dígitos en lugar de 10

                    // 2. Determinar el Precio Unitario
                    decimal precioUnitario = item.cantidad > 0 ? (decimal)item.precio / (decimal)item.cantidad : 0m;

                    // 3. Convertir a centavos (x 100) aplicando el redondeo bancario/fiscal antes del casteo
                    long precioCentavos = (long)Math.Round(precioUnitario * 100m, MidpointRounding.AwayFromZero);
                    string strPrecio = precioCentavos.ToString("0000000000");

                    // 4. Truncar descripción a un máximo de 37 caracteres
                    string descripcion = descrip.Length > 37 ? descrip.Substring(0, 37) : descrip;
                    string comandoVenta = $"{caracterIva}{strPrecio}{strCantidad}{descripcion}";

                    if (!SendCmd(comandoVenta))
                        throw new Exception($"Error en el artículo: {item.des_art}");

                    // Línea informativa del descuento del renglón (si aplica)
                    if (!string.IsNullOrWhiteSpace(item.porc_desc) && item.porc_desc.Trim() != "0")
                    {
                        try { SendCmd($"@Descuento: {item.porc_desc.Trim()}%"); }
                        catch (Exception exDesc) { LogArchivo($"No se pudo imprimir el descuento informativo del renglón: {exDesc.Message}"); }
                    }
                }

                if (!SendCmd("3"))
                    throw new Exception("Error al calcular el Subtotal de la factura");

                // --- CIERRE / MEDIOS DE PAGO ---
                if (factura.formas_pago == null || !factura.formas_pago.Any())
                {
                    if (!SendCmd("101"))
                        throw new Exception("Error en el cierre de la factura (Efectivo Bs)");
                }
                else
                {
                    // 1. Verificar si hay pagos en divisas (código fiscal entre 20 y 24)
                    bool tieneIgtf = factura.formas_pago.Any(f => 
                        int.TryParse(f.CodFis, out int cod) && cod >= 20 && cod <= 24);

                    if (tieneIgtf)
                    {
                        // Enviar líneas de comentario del IGTF antes de totalizar
                        SendCmd($"i{nLinea++:00}Esta condicion se genera de manera informativa");
                        SendCmd($"i{nLinea++:00}y solo aplica si el cobro de la misma cumple con");
                        SendCmd($"i{nLinea++:00}lo establecido en el Art 4 numeral 6 de la Ley IGTF");
                    }

                    // 2. Procesar cada forma de pago
                    for (int i = 0; i < factura.formas_pago.Count; i++)
                    {
                        var pago = factura.formas_pago[i];
                        string codigoFiscal = string.IsNullOrEmpty(pago.CodFis) ? "01" : pago.CodFis.PadLeft(2, '0');

                        // Si es el último pago de la lista, indicamos que pague el saldo remanente y cierre
                        if (i == factura.formas_pago.Count - 1)
                        {
                            // Cierra pagando el saldo remanente con la forma de pago real (código fiscal del front).
                            string comandoCierre = $"1{codigoFiscal}";
                            if (!SendCmd(comandoCierre))
                                throw new Exception($"Error al cerrar la factura con el medio de pago: {pago.DesFb}");
                        }
                        else
                        {
                            // Pago parcial (NO cierra): prefijo "2" como impfis; el "1XX" es solo para el
                            string strMonto = ((long)(pago.TotBs * 100)).ToString("000000000000");
                            string comandoPagoParcial = $"2{codigoFiscal}{strMonto}";

                            if (!SendCmd(comandoPagoParcial))
                                throw new Exception($"Error procesando pago parcial de: {pago.DesFb}");
                        }
                    }
                }

                // comando 199 = FINALIZAR documento en modo "flag 50".
                // impfis (con flag50.on) lo manda DESPUES del pago para que la impresora cierre
                // e imprima. Nuestra API nunca lo enviaba -> documento abierto -> cuelgue a mitad.
                if (factura.igtf == true)
                {
                    LogArchivo("Factura con IGTF, enviando comando 199 para finalizar documento.");
                    SendCmd("199");
                }

                // Leer el correlativo fiscal (frame S1) con el puerto aun abierto y el documento ya finalizado.
                string numFiscal = "";
                try
                {
                    numFiscal = LeerCampoS1(21, 8);
                    LogArchivo($"Nro fact fiscal: {numFiscal}");
                } catch(Exception ex)
                {
                    LogArchivo($"Nro fact error '{ex?.InnerException?.Message ?? ex?.Message}'");
                }

                CerrarPuertoSeguro();

                return Ok(new { status = true, mensaje = "Factura impresa correctamente.", numFiscal });


            }
            catch (Exception ex)
            {
                CerrarPuertoSeguro();

                return Ok(new
                {
                    status = false,
                    mensaje = ex.InnerException?.Message ?? ex.Message,
                    numFiscal = (string)null
                });
            }
        }

        // ESTE METODO ES SOLO PARA SABER QUE RECIBIMOS DE ELZYRA
        [HttpPost]
        [Route("PruebaFact")]
        public async Task<IActionResult> PruebaFact([FromBody] FacturaFiscal factura)
        {
            try
            {
                //await ConectarImp(factura.puerto);
                //await VerificarImpresoraLista();

                if (string.IsNullOrEmpty(factura.cliente.rif_cli)) throw new Exception($"La información de la factura esta incompleta, hace falta el rif del cliente");
                if (string.IsNullOrEmpty(factura.cliente.cli_des)) throw new Exception($"La información de la factura esta incompleta, hace falta la descripción del cliente");
                if (string.IsNullOrEmpty(factura.cliente.direc_cli)) throw new Exception($"La información de la factura esta incompleta, hace falta la dirección del cliente");
                if (!factura.items.Any()) throw new Exception($"La información de la factura esta incompleta, no tiene articulos a procesar");

                // --- REGISTRAR LOS ARTÍCULOS ---
                foreach (var item in factura.items)
                {
                    string caracterIva = item.tipo_iva switch
                    {
                        1 => "!",
                        2 => "\"",
                        3 => "#",
                        _ => " "
                    };

                    string strCantidad = item.cantidad.ToString();
                    string strPrecio = (item.cantidad > 0 ? item.precio / item.cantidad : 0).ToString();
                    string descripcion = item.des_art.Length > 37 ? item.des_art.Substring(0, 37) : item.des_art;
                    string comandoVenta = $"{caracterIva}{strPrecio}{strCantidad}{descripcion}";

                }

                // --- CIERRE / MEDIOS DE PAGO ---
                if (factura.formas_pago == null || !factura.formas_pago.Any())
                {

                }
                else
                {
                    // 1. Verificar si hay pagos en divisas (código fiscal entre 20 y 24)
                    bool tieneIgtf = factura.formas_pago.Any(f =>
                        int.TryParse(f.CodFis, out int cod) && cod >= 20 && cod <= 24);


                    // 2. Procesar cada forma de pago
                    for (int i = 0; i < factura.formas_pago.Count; i++)
                    {
                        var pago = factura.formas_pago[i];
                        string codigoFiscal = string.IsNullOrEmpty(pago.CodFis) ? "01" : pago.CodFis.PadLeft(2, '0');

                        // Si es el último pago de la lista, indicamos que pague el saldo remanente y cierre
                        if (i == factura.formas_pago.Count - 1)
                        {
                            string comandoCierre = $"1{codigoFiscal}";
                        }
                        else
                        {
                            string strMonto = ((long)(pago.TotBs * 100)).ToString("0000000000");
                            string comandoPagoParcial = $"1{codigoFiscal}{strMonto}";

                        }
                    }
                }

                return Ok(new { status = false, mensaje = "Factura impresa correctamente." });


            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    status = false,
                    mensaje = ex.InnerException?.Message ?? ex.Message
                });
            }
            finally
            {
                try
                {
                    //CloseFpctrl();
                }
                catch
                {
                }
            }
        }

        [HttpPost]
        [Route("ImprimirNcr")]
        public async Task<IActionResult> ImprimirNCR([FromBody] NotaCredito ncr)
        {
            try
            {
                // --- VALIDACIONES INICIALES ---
                if (ncr?.Cabecera == null)
                    throw new Exception("La información de la cabecera es requerida.");

                if (string.IsNullOrEmpty(ncr.Cabecera.CoCli))
                    throw new Exception("La información de la Nota de Crédito está incompleta, falta el RIF/Código del cliente.");

                if (ncr.Detalles == null || !ncr.Detalles.Any())
                    throw new Exception("La Nota de Crédito no contiene artículos a procesar.");

                string puerto = string.IsNullOrEmpty(ncr.Cabecera.PuertoFiscal) ? "COM4" : ncr.Cabecera.PuertoFiscal;

                // Recuperación defensiva: cerrar por si un run anterior dejó el puerto tomado.
                try { CloseFpctrl(); } catch { }

                bool connect = await ConectarImp(puerto);
                if (!connect)
                    throw new Exception($"No se logró establecer conexión con el puerto: {puerto}");

                await VerificarImpresoraLista();

                // --- ENVIAR DATOS DEL CLIENTE ---
                // 1. RIF/Cédula (En MAYÚSCULAS y sin guiones)
                string rif = ncr.Cabecera.CoCli.Trim().ToUpper().Replace("-", "");
                if (!SendCmd($"iR*{rif}"))
                    throw new Exception("Error enviando RIF del cliente");

                // 2. Razón Social (Máximo 40 caracteres)
                string razonSocial = ncr.Cabecera.DesCli.Trim();
                if (razonSocial.Length > 40) razonSocial = razonSocial.Substring(0, 40);
                if (!SendCmd($"iS*{razonSocial}"))
                    throw new Exception("Error enviando Razón Social");

                // Contador secuencial de líneas de comentario (iNN), como el lnlineae de impfis.
                int nLinea = 0;

                // 4. Dirección (Máximo 40 caracteres)
                if (!string.IsNullOrEmpty(ncr.Cabecera.DirecCli))
                {
                    string direccion = ncr.Cabecera.DirecCli.Trim();

                    // Si la dirección sobrepasa los 40 caracteres, la enviamos en 2 líneas de comentario.
                    if (direccion.Length > 40)
                    {
                        string dirLinea1 = direccion.Substring(0, 40);
                        string dirLinea2 = direccion.Substring(40, Math.Min(40, direccion.Length - 40));

                        SendCmd($"i{nLinea++:00}DIRECCION: {dirLinea1}");
                        SendCmd($"i{nLinea++:00}{dirLinea2}");
                    }
                    else
                    {
                        SendCmd($"i{nLinea++:00}DIRECCION: {direccion}");
                    }
                }
                if (!string.IsNullOrEmpty(ncr.Cabecera.DirecCli))
                {
                    string direccion = ncr.Cabecera.DirecCli.Trim();
                    if (direccion.Length > 40) direccion = direccion.Substring(0, 40);
                    SendCmd($"iD*{direccion}");
                }

                // --- ENVIAR DATOS DEL DOCUMENTO FISCAL AFECTADO (OBLIGATORIO EN NCR) ---
                // Número de factura que origina la devolución (8 dígitos de padding)
                string numDocAfectado = ncr.Cabecera.NumDoc.Trim().PadLeft(8, '0');
                if (!SendCmd($"iF:{numDocAfectado}")) // Sin asterisco
                    throw new Exception("Error enviando Número de Factura afectada");

                // Fecha de emisión de la factura afectada (Formato ddMMyy o dd-MM-yy)
                string fechaDocAfectado = ncr.Cabecera.FecEmis.ToString("ddMMyy");
                if (!SendCmd($"iD:{fechaDocAfectado}")) // Sin asterisco y formato ddMMyy
                    throw new Exception("Error enviando Fecha de Factura afectada");

                // Serial de la impresora que emitió la factura original
                if (!string.IsNullOrEmpty(ncr.Cabecera.Serial))
                {
                    string serialAfectado = ncr.Cabecera.Serial.Trim().ToUpper();
                    if (!SendCmd($"iI:{serialAfectado}")) // Sin asterisco
                        throw new Exception("Error enviando Serial de la Impresora afectada");
                }

                // --- REGISTRAR LOS ARTÍCULOS ---
                foreach (var item in ncr.Detalles)
                {
                    // Mapeo de alícuota a comandos de devolución (d0=Exento, d1=G 16%, d2=R 8%, d3=A 31%)
                    string caracterIvaDevolucion = item.TipoImp switch
                    {
                        1 => "d1",
                        2 => "d2",
                        3 => "d3",
                        _ => "d0"
                    };

                    item.TotalDevolver = item.TotalDevolver == 0m ? item.TotalArt : item.TotalDevolver;
                    // Cantidad formateada con Math.Round (usando el multiplicador que te funcionó en Factura, ej: x100 o x1000)
                    long cantidadMilensimas = (long)Math.Round(item.TotalDevolver * 1000m, MidpointRounding.AwayFromZero);
                    string strCantidad = cantidadMilensimas.ToString("00000000"); // 8 DÍGITOS

                    // Precio UNITARIO en Bs con descuento aplicado (desc_vol1 = % de descuento, ej. 20.00 = 20%),
                    // formateado a 10 dígitos (x100). Se usa el unitario (NO RengNetoBs, que es el neto del renglón
                    // y ya incluye la cantidad) porque la impresora multiplica precio x cantidad.
                    decimal precioUnitarioNeto = item.PrecioBs * (1m - (item.DescVol1 / 100m));
                    long precioCalculado = (long)Math.Round(precioUnitarioNeto * 100m, MidpointRounding.AwayFromZero);
                    string strPrecio = precioCalculado.ToString("0000000000");

                    // Truncado de descripción a 37 caracteres
                    string descripcion = item.DesArt.Trim();
                    if (descripcion.Length > 37) descripcion = descripcion.Substring(0, 37);

                    string comandoDevolucion = $"{caracterIvaDevolucion}{strPrecio}{strCantidad}{descripcion}";

                    if (!SendCmd(comandoDevolucion))
                        throw new Exception($"Error en la devolución del artículo: {item.DesArt}");
                }

                // --- CIERRE DE NOTA DE CRÉDITO ---
                // FIX: esperar el fin real del documento antes de cerrar el puerto
                if (!SendCmd("101"))
                    throw new Exception("Error en el cierre de la Nota de Crédito");

                // FINALIZAR documento (modo flag 50): impfis manda 199 tras el cierre para que la NC
                // finalice e imprima. Sin esto la NC queda abierta y se cuelga (igual que pasaba en factura).
                if (ncr.Cabecera.igtf == true)
                {
                    LogArchivo("Nota de Crédito con IGTF, enviando comando 199 para finalizar documento.");
                    SendCmd("199");
                }

                // Leer el correlativo fiscal de la NC (frame S1, contador de notas de credito).
                string numFiscal = LeerCampoS1(47, 8);

                CerrarPuertoSeguro();

                return Ok(new { status = true, mensaje = "Nota de Crédito impresa correctamente.", numFiscal });
            }
            catch (Exception ex)
            {
                CerrarPuertoSeguro();

                return Ok(new
                {
                    status = false,
                    mensaje = ex.InnerException?.Message ?? ex.Message,
                    numFiscal = (string)null
                });
            }
        }


        // ESTE METODO ES SOLO PARA SABER QUE RECIBIMOS DE ELZYRA
        [HttpPost]
        [Route("PruebaNcr")]
        public async Task<IActionResult> PruebaNcr([FromBody] NotaCredito ncr)
        {
            try
            {
                // --- VALIDACIONES INICIALES ---
                if (ncr?.Cabecera == null)
                    throw new Exception("La información de la cabecera es requerida.");

                if (string.IsNullOrEmpty(ncr.Cabecera.CoCli))
                    throw new Exception("La información de la Nota de Crédito está incompleta, falta el RIF/Código del cliente.");

                if (ncr.Detalles == null || !ncr.Detalles.Any())
                    throw new Exception("La Nota de Crédito no contiene artículos a procesar.");

                string puerto = string.IsNullOrEmpty(ncr.Cabecera.PuertoFiscal) ? "COM4" : ncr.Cabecera.PuertoFiscal;


                // --- ENVIAR DATOS DEL CLIENTE ---
                // 1. RIF/Cédula (En MAYÚSCULAS y sin guiones)
                string rif = ncr.Cabecera.CoCli.Trim().ToUpper().Replace("-", "");

                // 2. Razón Social (Máximo 40 caracteres)
                string razonSocial = ncr.Cabecera.DesCli.Trim();
                if (razonSocial.Length > 40) razonSocial = razonSocial.Substring(0, 40);

                // 3. Teléfono (si existe, usar iF* o i02)
                if (!string.IsNullOrEmpty(ncr.Cabecera.TlfCli))
                {
                    string tlf = new string(ncr.Cabecera.TlfCli.Where(char.IsDigit).ToArray());
                    if (tlf.Length > 20) tlf = tlf.Substring(0, 20);
                }

                // 4. Dirección (Máximo 40 caracteres)
                if (!string.IsNullOrEmpty(ncr.Cabecera.DirecCli))
                {
                    string direccion = ncr.Cabecera.DirecCli.Trim();
                    if (direccion.Length > 40) direccion = direccion.Substring(0, 40);
                }

                // --- ENVIAR DATOS DEL DOCUMENTO FISCAL AFECTADO (OBLIGATORIO EN NCR) ---
                // Número de factura que origina la devolución (8 dígitos de padding)
                string numDocAfectado = ncr.Cabecera.NumDoc.Trim().PadLeft(8, '0');

                // Fecha de emisión de la factura afectada (Formato ddMMyy o dd-MM-yy)
                string fechaDocAfectado = ncr.Cabecera.FecEmis.ToString("ddMMyy");

                // Serial de la impresora que emitió la factura original
                if (!string.IsNullOrEmpty(ncr.Cabecera.Serial))
                {
                    string serialAfectado = ncr.Cabecera.Serial.Trim().ToUpper();
                }

                // --- REGISTRAR LOS ARTÍCULOS ---
                foreach (var item in ncr.Detalles)
                {
                    // Mapeo de alícuota a comandos de devolución (d0=Exento, d1=G 16%, d2=R 8%, d3=A 31%)
                    string caracterIvaDevolucion = item.TipoImp switch
                    {
                        1 => "d1",
                        2 => "d2",
                        3 => "d3",
                        _ => "d0"
                    };

                    item.TotalDevolver = item.TotalDevolver == 0m ? item.TotalArt : item.TotalDevolver;
                    // Cantidad formateada con Math.Round (usando el multiplicador que te funcionó en Factura, ej: x100 o x1000)
                    long cantidadMilensimas = (long)Math.Round(item.TotalDevolver * 1000m, MidpointRounding.AwayFromZero);
                    string strCantidad = cantidadMilensimas.ToString("00000000"); // 8 DÍGITOS

                    // Precio UNITARIO en Bs con descuento aplicado (desc_vol1 = % de descuento, ej. 20.00 = 20%),
                    // formateado a 10 dígitos (x100). Se usa el unitario (NO RengNetoBs, que es el neto del renglón
                    // y ya incluye la cantidad) porque la impresora multiplica precio x cantidad.
                    decimal precioUnitarioNeto = item.PrecioBs * (1m - (item.DescVol1 / 100m));
                    long precioCalculado = (long)Math.Round(precioUnitarioNeto * 100m, MidpointRounding.AwayFromZero);
                    string strPrecio = precioCalculado.ToString("0000000000");

                    // Truncado de descripción a 37 caracteres
                    string descripcion = item.DesArt.Trim();
                    if (descripcion.Length > 37) descripcion = descripcion.Substring(0, 37);

                    string comandoDevolucion = $"{caracterIvaDevolucion}{strPrecio}{strCantidad}{descripcion}";

                }

                return Ok(new { status = false, mensaje = "Nota de Crédito impresa correctamente." });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    status = false,
                    mensaje = ex.InnerException?.Message ?? ex.Message
                });
            }
            finally
            {
                try
                {
                }
                catch
                {
                }
            }
        }

        [HttpPost]
        [Route("ImprimirReporte/{puerto}")]
        public async Task<IActionResult> ImprimirReporte(string puerto, [FromBody] ReporteRequest request)
        {
            string reporte = request?.Reporte;
            try
            {
                // 1. VALIDAR PARÁMETRO
                if (string.IsNullOrWhiteSpace(reporte))
                    throw new Exception("El tipo de reporte es requerido (X o Z)");

                string tipoUpper = reporte.Trim().ToUpper();
                if (tipoUpper != "X" && tipoUpper != "Z")
                    throw new Exception("El reporte debe ser 'X' (reporte de ventas) o 'Z' (cierre fiscal)");

                // 2. CONECTAR A LA IMPRESORA
                bool connect = await ConectarImp(puerto);
                if (!connect)
                    throw new Exception($"No se logró establecer conexión con el puerto: {puerto}");

                // 3. VERIFICAR QUE LA IMPRESORA ESTÉ LISTA
                await VerificarImpresoraLista();

                // 4. ENVIAR COMANDO HKA (I0X = Reporte X, I0Z = Reporte Z)
                string comandoReporte = tipoUpper == "X" ? "I0X" : "I0Z";

                bool result = SendCmd(comandoReporte);
                if (!result)
                    throw new Exception($"No se logró emitir el Reporte {tipoUpper}. Asegúrate de que no haya un documento abierto.");

                // 5. OBTENER NUMERO Z (del frame de estado S1) PARA REPORTE Z
                string numeroZ = string.Empty;
                string mensajeNumeroZ = string.Empty;
                if (tipoUpper == "Z")
                {
                    await Task.Delay(3000); // dar tiempo a que la impresora complete el cierre fiscal

                    // El numero del ultimo reporte Z esta en el frame S1: SUBSTR(61,4) -> (60,4).
                    numeroZ = LeerCampoS1(60, 4);
                    mensajeNumeroZ = string.IsNullOrEmpty(numeroZ)
                        ? "No se pudo leer el numero Z del frame S1"
                        : "Numero Z leido del frame S1";
                }

                return Ok(new
                {
                    status = true,
                    tipo = tipoUpper == "X" ? "Reporte X (Lectura)" : "Reporte Z (Cierre Fiscal)",
                    numeroZ = string.IsNullOrEmpty(numeroZ) ? null : numeroZ,
                    mensaje = string.IsNullOrEmpty(mensajeNumeroZ) ? null : mensajeNumeroZ
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    status = false,
                    tipo = reporte?.ToUpper() == "X" ? "Reporte X (Lectura)" : "Reporte Z (Cierre Fiscal)",
                    numeroZ = "",
                    mensaje = $"Error: {ex.InnerException?.Message ?? ex.Message}"
                });
            }
            finally
            {
                try
                {
                    CloseFpctrl();
                }
                catch
                {
                    // Evita que un error de puerto oculte el resultado
                }
            }
        }

        [HttpPost]
        [Route("reconciliarZ")]
        public async Task<IActionResult> ReconciliarZ([FromBody] List<RegistrosCierresValidarZ> registros)
        {
            List<RegistrosCierresValidarZ> reportesImpresos = new List<RegistrosCierresValidarZ>();
            List<RegistrosCierresValidarZ> reportesFallidos = new List<RegistrosCierresValidarZ>();
            string mensajeGeneral = string.Empty;
            bool todosExitosos = true;
            string puertoUsado = string.Empty;

            try
            {
                // 1. VALIDACIONES INICIALES
                if (registros == null || !registros.Any())
                    throw new Exception("La lista de registros a reconciliar está vacía.");

                // Validar fecha mínima permitida (29 de Julio de 2026)
                DateTime fechaMinimaPermitida = new DateTime(2026, 7, 29);
                if (registros.Any(r => r.FechaCierre.Date < fechaMinimaPermitida))
                {
                    throw new Exception("Registros por debajo del 29 de julio de 2026 no son válidos para este proceso.");
                }

                // Verificar que todos los registros tengan el mismo puerto
                var puertosDistintos = registros.Select(r => r.PuertoFiscal).Distinct();
                if (puertosDistintos.Count() > 1)
                    throw new Exception("Todos los registros deben pertenecer al mismo puerto fiscal.");

                // Verificar que todos los registros tengan el mismo cajero
                var cajerosDistintos = registros.Select(r => r.IdCajero).Distinct();
                if (cajerosDistintos.Count() > 1)
                    throw new Exception("Todos los registros deben pertenecer al mismo cajero.");

                // Verificar que todos los registros tengan el mismo serial
                var serialesDistintos = registros.Select(r => r.Serial).Distinct();
                if (serialesDistintos.Count() > 1)
                    throw new Exception("Todos los registros deben pertenecer al mismo serial de impresora.");

                // 2. OBTENER PUERTO
                puertoUsado = $"COM{registros.First().PuertoFiscal}";

                // 3. CONECTAR A LA IMPRESORA
                bool connect = await ConectarImp(puertoUsado);
                if (!connect)
                    throw new Exception($"No se logró establecer conexión con el puerto: {puertoUsado}");

                // 4. VERIFICAR QUE LA IMPRESORA ESTÉ LISTA
                await VerificarImpresoraLista();

                // 5. PROCESAR CADA REGISTRO
                foreach (var registro in registros)
                {
                    try
                    {
                        // Validar que el registro tenga fecha
                        if (registro.FechaCierre == DateTime.MinValue)
                        {
                            registro.FueReconciliado = false;
                            reportesFallidos.Add(registro);
                            todosExitosos = false;
                            continue;
                        }

                        // --- IMPRIMIR REPORTE Z ---
                        // Enviar comando I0Z
                        bool result = SendCmd("I0Z");
                        if (!result)
                        {
                            registro.FueReconciliado = false;
                            reportesFallidos.Add(registro);
                            todosExitosos = false;
                            continue;
                        }

                        // Esperar a que termine la impresión
                        await Task.Delay(3000);

                        // --- OBTENER NÚMERO Z ---
                        string numeroZ = string.Empty;
                        try
                        {
                            // Intentar obtener el número Z con comando "91"
                            string respuestaCierre = SendCmdWithResponse("91");
                            if (!string.IsNullOrEmpty(respuestaCierre))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(respuestaCierre, @"\d+");
                                if (match.Success)
                                {
                                    numeroZ = match.Value;
                                }
                            }

                            // Si no se obtuvo, intentar con el estado
                            if (string.IsNullOrEmpty(numeroZ))
                            {
                                int status = 0;
                                int error = 0;
                                int resultadoStatus = await Task.Run(() => ReadFpStatus(ref status, ref error));
                                if (resultadoStatus == 1)
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(status.ToString(), @"\d+");
                                    if (match.Success)
                                    {
                                        numeroZ = match.Value;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Si falla la obtención del número Z, continuamos igual
                        }

                        // Actualizar el registro con el número Z obtenido
                        registro.NumeroZ = string.IsNullOrEmpty(numeroZ) ? "No disponible" : numeroZ;
                        registro.FueReconciliado = true;
                        reportesImpresos.Add(registro);
                    }
                    catch (Exception ex)
                    {
                        // Si falla un registro específico, lo marcamos como fallido
                        registro.FueReconciliado = false;
                        reportesFallidos.Add(registro);
                        todosExitosos = false;
                    }
                }

                // 6. CONSTRUIR MENSAJE
                if (todosExitosos)
                {
                    mensajeGeneral = $"Todos los reportes Z reconciliados exitosamente. Total: {reportesImpresos.Count}";
                }
                else
                {
                    mensajeGeneral = $"Se procesaron {registros.Count} reportes. Éxitos: {reportesImpresos.Count}, Fallidos: {reportesFallidos.Count}";
                }

                // 7. CONSTRUIR RESPUESTA
                return Ok(new
                {
                    status = todosExitosos,
                    mensaje = mensajeGeneral,
                    reportes = reportesImpresos,
                    fallidos = reportesFallidos,
                    totalProcesados = registros.Count,
                    totalExitosos = reportesImpresos.Count,
                    totalFallidos = reportesFallidos.Count,
                    puerto = puertoUsado
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    status = false,
                    mensaje = $"Error en el proceso de reconciliación: {ex.InnerException?.Message ?? ex.Message}",
                    reportes = reportesImpresos,
                    fallidos = reportesFallidos,
                    totalProcesados = registros?.Count ?? 0,
                    totalExitosos = reportesImpresos.Count,
                    totalFallidos = reportesFallidos.Count,
                    puerto = puertoUsado
                });
            }
            finally
            {
                try
                {
                    CloseFpctrl();
                }
                catch
                {
                    // Evita que un error de puerto oculte el resultado
                }
            }
        }
        #endregion

        #region METHODS
        private async Task<bool> ConectarImp(string puerto)
        {
            int resultado = await Task.Run(() => OpenFpctrl(puerto));

            if (resultado != 1)
                throw new Exception($"No se logró conectar al puerto {puerto}. Código devuelto por la DLL: {resultado}");

            return true;
        }


        private async Task<string> ObtenerEstado(string puerto)
        {
            await ConectarImp(puerto);
            await VerificarImpresoraLista();

            try
            {
                int s1 = 0;
                int s2 = 0;

                int resultado = await Task.Run(() => ReadFpStatus(ref s1, ref s2));

                if (resultado == 1)
                {
                    int flagsEstado = s1 & 0x7F;

                    string adicional = "";
                    if (System.IO.File.Exists("status.txt"))
                    {
                        try
                        {
                            string cont = await System.IO.File.ReadAllTextAsync("status.txt");
                            adicional = $" | status.txt: {cont.Trim()}";
                        }
                        catch { }
                    }

                    return $"OK - S1: {s1}, S2: {s2} (Flags: {flagsEstado}){adicional}";
                }
                else
                {
                    return $"ERROR_DLL: Código de retorno {resultado}, S1: {s1}, S2: {s2}";
                }
            }
            catch (Exception ex)
            {
                return $"ERROR_EXCEPCION: {ex.InnerException.Message ?? ex.Message}";
            }
        }

        private async Task<object> ObtenerInfoDLL()
        {
            string ruta = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "DLLs",
                "tfhkaif.dll");

            bool existe = await Task.Run(() => System.IO.File.Exists(ruta));

            return new
            {
                encontrada = existe,
                ruta = ruta,
                arquitecturaSO = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits",
                arquitecturaProceso = Environment.Is64BitProcess ? "64 bits" : "32 bits",
                framework = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription
            };
        }


        private bool SendCmd(string comando)
        {
            int status = 0;
            int error = 0;

            int resultado = SendCmd(ref status, ref error, comando);

            // DIAGNOSTICO: registrar el estado de la impresora DESPUES de cada comando,
            // para ver en cual se prende el bit de buffer (0x04) y deja de drenar.
            bool bufferLleno = (status & 0x04) == 0x04;
            LogArchivo($"SendCmd '{comando}' -> ret={resultado} status={status} error={error} bufferLleno={bufferLleno} | {GetStatusError(status, error)}");

            if (resultado != 1)
            {
                throw new Exception(
                    $"Error enviando comando '{comando}'. Resultado: {resultado}, Status: {status}, Error: {error}");
            }

            return true;
        }

        private async Task VerificarImpresoraLista()
        {
            int status = 0;
            int error = 0;

            int resultado = await Task.Run(() => ReadFpStatus(ref status, ref error));

            if (resultado != 1)
            {
                throw new Exception(
                    $"No fue posible leer el estado de la impresora. Resultado: {resultado}");
            }

            if (error != 0)
            {
                throw new Exception(
                    $"La impresora reportó un error. Status: {status}, Error: {error}");
            }
        }

        // METODO PARA OBTENER ULTIMO Z, EL COMANDO QUE SE LE ENVIA ES EL 91
        private string SendCmdWithResponse(string comando)
        {
            try
            {
                // Estrategia 1: Usar SendCmd directamente y verificar si la respuesta
                // se almacena en algún parámetro de salida
                int status = 0;
                int error = 0;

                int resultado = SendCmd(ref status, ref error, comando);

                if (resultado == 1)
                {
                    // Si el comando fue exitoso, el error podría contener la respuesta
                    if (error != 0)
                    {
                        return error.ToString();
                    }

                    // O el status podría contener información
                    if (status != 0)
                    {
                        return status.ToString();
                    }
                }

                // Estrategia 2: Si la DLL tiene un buffer de respuesta interno,
                // podríamos intentar leerlo a través de una función no documentada
                // (Esto es un intento, puede que no funcione)
                try
                {
                    // Algunas DLLs tienen un método para obtener el último error
                    // o la última respuesta en un StringBuilder
                    var sb = new System.Text.StringBuilder(256);
                    // Intenta llamar a una función que podría existir
                    // GetLastResponse(sb, sb.Capacity);
                    // return sb.ToString();
                }
                catch { }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Lee el frame de estado "S1" de la impresora fiscal (lo vuelca a un archivo via la DLL y lo lee).
        // Requiere el puerto ABIERTO. Devuelve el frame completo como texto, o null si falla.
        private string LeerFrameS1()
        {
            try
            {
                int status = 0;
                int error = 0;
                string ruta = Path.Combine(AppContext.BaseDirectory, "s1frame.txt");

                int r = UploadStatusCmd(ref status, ref error, "S1", ruta);
                LogArchivo($"LeerFrameS1: UploadStatusCmd ret={r} status={status} error={error}");

                if (r != 1 || !System.IO.File.Exists(ruta)) return null;

                string frame = System.IO.File.ReadAllText(ruta);
                LogArchivo($"LeerFrameS1: frame='{frame}'");
                return frame;
            }
            catch (Exception ex)
            {
                LogArchivo($"LeerFrameS1: excepcion {ex.Message}");
                return null;
            }
        }

        // Extrae un campo del frame S1 por offset (base 0) y longitud. Offsets (base 1 en VFP):
        //   factura fiscal = SUBSTR(22,8) -> (21,8) | nota de credito = SUBSTR(48,8) -> (47,8)
        //   reporte Z      = SUBSTR(61,4) -> (60,4)
        private string LeerCampoS1(int offset, int len)
        {
            string frame = LeerFrameS1();
            if (string.IsNullOrEmpty(frame) || frame.Length < offset + len) return null;
            return frame.Substring(offset, len).Trim();
        }

        private void CerrarPuertoSeguro()
        {
            try
            {
                // Pequeña pausa antes de cerrar para asegurar que la impresora termine
                Thread.Sleep(500);
                CloseFpctrl();
                Console.WriteLine("[IMPRESORA] Puerto cerrado correctamente");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IMPRESORA] Error al cerrar puerto: {ex.Message}");
            }
        }

        private string GetStatusError(int st, int er)
        {
            int st_aux = st;
            st = st & ~0x04;

            string statusMsg = "";
            string statusNum = "";

            if ((st & 0x6A) == 0x6A)
            {
                statusMsg = "En modo fiscal, carga completa de la memoria fiscal y emisión de documentos no fiscales";
                statusNum = "12";
            }
            else if ((st & 0x69) == 0x69)
            {
                statusMsg = "En modo fiscal, carga completa de la memoria fiscal y emisión de documentos fiscales";
                statusNum = "11";
            }
            else if ((st & 0x68) == 0x68)
            {
                statusMsg = "En modo fiscal, carga completa de la memoria fiscal y en espera";
                statusNum = "10";
            }
            else if ((st & 0x72) == 0x72)
            {
                statusMsg = "En modo fiscal, cercana carga completa de la memoria fiscal y en emisión de documentos no fiscales";
                statusNum = "9";
            }
            else if ((st & 0x71) == 0x71)
            {
                statusMsg = "En modo fiscal, cercana carga completa de la memoria fiscal y en emisión de documentos no fiscales";
                statusNum = "8";
            }
            else if ((st & 0x70) == 0x70)
            {
                statusMsg = "En modo fiscal, cercana carga completa de la memoria fiscal y en espera";
                statusNum = "7";
            }
            else if ((st & 0x62) == 0x62)
            {
                statusMsg = "En modo fiscal y en emisión de documentos no fiscales";
                statusNum = "6";
            }
            else if ((st & 0x61) == 0x61)
            {
                statusMsg = "En modo fiscal y en emisión de documentos fiscales";
                statusNum = "5";
            }
            else if ((st & 0x60) == 0x60)
            {
                statusMsg = "En modo fiscal y en espera";
                statusNum = "4";
            }
            else if ((st & 0x42) == 0x42)
            {
                statusMsg = "En modo prueba y en emisión de documentos no fiscales";
                statusNum = "3";
            }
            else if ((st & 0x41) == 0x41)
            {
                statusMsg = "En modo prueba y en emisión de documentos fiscales";
                statusNum = "2";
            }
            else if ((st & 0x40) == 0x40)
            {
                statusMsg = "En modo prueba y en espera";
                statusNum = "1";
            }
            else if ((st & 0x00) == 0x00)
            {
                statusMsg = "Status Desconocido";
                statusNum = "0";
            }

            string errorMsg = "";
            string errorNum = "";

            if ((er & 0x6C) == 0x6C)
            {
                errorMsg = "Memoria Fiscal llena";
                errorNum = "108";
            }
            else if ((er & 0x64) == 0x64)
            {
                errorMsg = "Error en memoria fiscal";
                errorNum = "100";
            }
            else if ((er & 0x60) == 0x60)
            {
                errorMsg = "Error Fiscal";
                errorNum = "96";
            }
            else if ((er & 0x5C) == 0x5C)
            {
                errorMsg = "Comando Invalido";
                errorNum = "92";
            }
            else if ((er & 0x58) == 0x58)
            {
                errorMsg = "No hay asignadas directivas";
                errorNum = "88";
            }
            else if ((er & 0x54) == 0x54)
            {
                errorMsg = "Tasa Invalida";
                errorNum = "84";
            }
            else if ((er & 0x50) == 0x50)
            {
                errorMsg = "Comando Invalido/Valor Invalido";
                errorNum = "80";
            }
            else if ((er & 0x43) == 0x43)
            {
                errorMsg = "Fin en la entrega de papel y error mecánico";
                errorNum = "3";
            }
            else if ((er & 0x42) == 0x42)
            {
                errorMsg = "Error de indole mecanico en la entrega de papel";
                errorNum = "2";
            }
            else if ((er & 0x41) == 0x41)
            {
                errorMsg = "Fin en la entrega de papel";
                errorNum = "1";
            }
            else if ((er & 0x40) == 0x40)
            {
                errorMsg = "Sin error";
                errorNum = "0";
            }

            if ((st_aux & 0x04) == 0x04)
            {
                errorMsg = "Buffer Completo";
                errorNum = "112";
            }
            else if (er == 128)
            {
                errorMsg = "CTS en falso (Error en la comunicación)";
                errorNum = "128";
            }
            else if (er == 137)
            {
                errorMsg = "No hay respuesta";
                errorNum = "137";
            }
            else if (er == 144)
            {
                errorMsg = "Error LRC";
                errorNum = "144";
            }
            else if (er == 114)
            {
                errorMsg = "Impresora no responde o esta ocupada";
                errorNum = "114";
            }

            return $"Estado: {statusNum} ({statusMsg}) | Error: {errorNum} ({errorMsg})";
        }

        private int ObtenerEstadoImpresora()
        {
            int status = 0;
            int error = 0;

            try
            {
                int resultado = ReadFpStatus(ref status, ref error);

                // Si el llamado a la DLL no fue exitoso (resultado != 1 o != 0 según tu versión de DLL)
                // se retorna status para evaluarlo
                return status;
            }
            catch
            {
                return -1; // Error de lectura o fallo del puerto
            }
        }

        private bool EsEstadoListo(int status)
        {
            // En las impresoras The Factory HKA / Bixolon:
            // Status 0: Modo Standby / Lista en espera de comandos.
            // Status 1 o 2: Documento Abierto (Factura / NCR en proceso de registro de ítems).
            // Status 3: Documento Abierto en etapa de Pagos / Cierre.
            // Status 4: Ocupada ejecutando impresión / corte / avance de papel.
            //
            // Para enviar el siguiente comando del buffer, la impresora debe estar lista (Status 0, 1, 2 o 3)
            // y NO debe estar ejecutando un proceso físico pendiente ni con error crítico.

            if (status == -1) return false;

            // Evaluamos que NO esté realizando una tarea física en segundo plano
            // Status 4 suele indicar "Impresora Ocupada / Imprimiendo"
            bool estaOcupada = (status == 4);

            return !estaOcupada;
        }

        // DIAGNOSTICO: log a archivo. Corriendo como Servicio de Windows, Console no es visible,
        // asi que dejamos el rastro en un .txt junto al ejecutable (AppContext.BaseDirectory).
        private static readonly object _logLock = new object();

        private static void LogArchivo(string mensaje)
        {
            try
            {
                string ruta = Path.Combine(AppContext.BaseDirectory, "log-impresion-fiscal.txt");
                string linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {mensaje}{Environment.NewLine}";
                lock (_logLock)
                {
                    System.IO.File.AppendAllText(ruta, linea);
                }
            }
            catch
            {
                // El logging nunca debe interrumpir la impresion.
            }
        }

        private async Task EsperarFinImpresion(int timeoutMs = 30000)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            LogArchivo($"EsperarFinImpresion: INICIO (timeout {timeoutMs}ms)");

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                int status = 0;
                int error = 0;
                int resultado = ReadFpStatus(ref status, ref error);

                string decodificado = resultado == 1
                    ? GetStatusError(status, error)
                    : $"ReadFpStatus fallo (ret={resultado})";

                LogArchivo($"  t={stopwatch.ElapsedMilliseconds}ms | ret={resultado} status={status} error={error} | {decodificado}");

                if (resultado == 1)
                {
                    // Interpretación por máscara de bits (misma convención que GetStatusError):
                    //   bit 0x40  -> modo fiscal
                    //   bits 0x03 -> fase del documento: 00 = en espera (terminó),
                    //                01 = emitiendo doc fiscal, 10 = emitiendo doc no fiscal
                    bool enModoFiscal = (status & 0x40) == 0x40;
                    bool enEspera = (status & 0x03) == 0;

                    if (enModoFiscal && enEspera)
                    {
                        LogArchivo($"EsperarFinImpresion: DOCUMENTO FINALIZADO (status={status})");
                        return;
                    }
                }

                // Poll suave: no saturar la línea serial mientras la impresora imprime el cierre
                await Task.Delay(1500);
            }

            LogArchivo("EsperarFinImpresion: TIMEOUT esperando fin de impresion");
        }

        private async Task<bool> SendCmdConPolling(string cmd, int timeoutMs = 30000)
        {
            // 1. Enviar el comando de cierre
            LogArchivo($"SendCmdConPolling: enviando comando de cierre '{cmd}'");
            if (!SendCmd(cmd))
            {
                LogArchivo($"SendCmdConPolling: SendCmd('{cmd}') devolvio false");
                return false;
            }
            LogArchivo($"SendCmdConPolling: comando '{cmd}' aceptado; esperando fin de impresion...");

            // 2. Esperar a que termine de imprimir
            await EsperarFinImpresion(timeoutMs);

            return true;
        }

        #endregion
    }
}
