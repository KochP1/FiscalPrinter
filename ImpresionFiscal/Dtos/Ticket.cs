namespace ImpresionFiscal.Dtos
{
    public class Ticket
    {
        public class ImpresionRequest
        {
            public string PrinterName { get; set; }
            public string EscPosCode { get; set; }
        }

        public class ImpresionIPRequest
        {
            public string PrinterIp { get; set; }
            public string EscPosCode { get; set; }
        }

        // El front manda solo el ESC/POS; el servicio resuelve la impresora predeterminada
        // de la maquina y decide IP (TCP 9100) vs USB. Ver ImprimirAuto.
        public class ImpresionAutoRequest
        {
            public string EscPosCode { get; set; }
        }

        public class ImpresoraInfo
        {
            public string Name { get; set; }
            public string DriverName { get; set; }
            public string PortName { get; set; }
            public bool Default { get; set; }
            public bool Shared { get; set; }
            public string Location { get; set; }
            public string Comment { get; set; }
        }

        public class ImpresoraPredeterminadaRequest
        {
            public string Nombre { get; set; }
        }

        // Resultado de cambiar la impresora predeterminada. `Predeterminada` se relee DESPUES de
        // aplicar el cambio: es lo unico que prueba que quedo, en vez de confiar en que la
        // llamada no dio error.
        public class ImpresoraPredeterminadaRes
        {
            public bool Exito { get; set; }
            public string Mensaje { get; set; }
            public string Predeterminada { get; set; }
        }

        public class ResultInit
        {
            public byte[] Reporte { get; set; } /*Arreglo de bytes de los reportes generados*/
            public string Resultado { get; set; } /*Resultado, "se llena cuando hay un error"*/
            public bool StatusOperacion { get; set; } /*Status true que todo salio bien, Status false que algo salio mal, revisar el Resultado*/
        }
    }
}
