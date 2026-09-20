namespace ImpresionFiscal.Dtos
{
    public class EstadoDetallado
    {
        public int S1 { get; set; }          // Código de estado
        public int S2 { get; set; }          // Código de documento abierto
        public int CodigoError { get; set; } // Código de error
        public int NumeroZ { get; set; }     // Último Z generado
        public bool TieneDocumentoAbierto => S2 == 1 || S2 == 3 || S2 == 4 || S2 == 5;
        public bool EstaReady => S1 == 0 && S2 == 0 && CodigoError == 0;
        public bool RequiereCierreZ => S1 == 7;

        public string DescripcionEstado
        {
            get
            {
                // Reutilizar tu lógica actual aquí
                return ObtenerDescripcionEstado(S1, S2, CodigoError);
            }
        }

        private string ObtenerDescripcionEstado(int s1, int s2, int error)
        {
            // Tu lógica actual de ObtenerEstado() aquí
            if (error > 0) return $"ERROR_{error}";
            return s1 switch
            {
                0 => "READY",
                1 => "MODO_ENTRENAMIENTO",
                2 => "DOCUMENTO_NO_FISCAL",
                3 => "FACTURA_ABIERTA",
                4 => "NOTA_CREDITO_ABIERTA",
                5 => "NOTA_DEBITO_ABIERTA",
                6 => "MEMORIA_LLENA",
                7 => "CIERRE_Z_REQUERIDO",
                _ => $"ESTADO_DESCONOCIDO_{s1}"
            };
        }
    }
}
