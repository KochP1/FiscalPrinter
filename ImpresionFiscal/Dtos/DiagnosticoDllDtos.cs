namespace ImpresionFiscal.Dtos
{
    public class DiagnosticoDllDtos
    {
        public class VerificacionComando
        {
            public string Comando { get; set; } = string.Empty;
            public bool Verificado { get; set; }
            public string Mensaje { get; set; } = string.Empty;
            public bool TieneRespuesta { get; set; }
            public string Respuesta { get; set; } = string.Empty;
        }

        public class ReporteSoportado
        {
            public bool SoportaComandoSimple { get; set; }
            public bool SoportaComandoExtendido { get; set; }
            public bool SoportaXSimple { get; set; }
            public bool SoportaXExtendido { get; set; }
            public bool SoportaCancelacion { get; set; }
            public bool SoportaLecturaZ { get; set; }
            public bool SoportaVersion { get; set; }
            public string ComandoRecomendadoZ { get; set; } = string.Empty;
            public string ComandoRecomendadoX { get; set; } = string.Empty;
            public string Mensaje { get; set; } = string.Empty;
        }
    }
}
