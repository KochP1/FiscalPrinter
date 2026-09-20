namespace ImpresionFiscal.Dtos
{
    public class PuertoFiscal
    {
        // Un puerto serie de esta maquina, tal como lo describe Windows.
        public class PuertoSerieInfo
        {
            public string Puerto { get; set; }
            public string Descripcion { get; set; }
            public string Fabricante { get; set; }
            // VID/PID del adaptador cuando el puerto es USB. Sirve para reconocer el mismo
            // cable en otra caja aunque Windows le asigne otro numero de COM.
            public string IdHardware { get; set; }
            // Este puerto se identifico como la impresora fiscal.
            public bool CoincideFiscal { get; set; }
            // Como se identifico: "id-hardware" (VID/PID del puente USB-serie) o "firma"
            // (texto en la descripcion o el fabricante). Null si no coincidio.
            public string MotivoCoincidencia { get; set; }
            // Nombre del modelo que se le atribuye segun la entrada de PuertoFiscal:Modelos que
            // coincidio (ej. "HKA80"). Null si no coincidio ninguna.
            //
            // Es el nombre CONFIGURADO para ese hardware, no el que declara la impresora:
            // Windows no conoce el modelo (ver ModeloFiscalConfig) y no se le pregunta a la
            // impresora. Si una tienda tiene otro modelo sobre el mismo puente USB-serie, va a
            // decir este nombre igual.
            public string Modelo { get; set; }
        }

        // Resultado de intentar deducir en que COM esta la impresora fiscal.
        //
        // `Puerto` viene con valor SOLO cuando la deteccion es concluyente. Cuando no lo es,
        // viene en null y `Candidatos` trae lo que quedo por decidir: es mejor que la pantalla de
        // configuracion muestre tres opciones que que la API invente una.
        //
        // `Candidatos` NO es el inventario de puertos de la maquina. Cuando la fiscal se reconoce,
        // trae un solo elemento: el de la fiscal. Trae varios solo cuando no se pudo decidir.
        // Un modelo de impresora fiscal y como reconocerlo. Se configura en PuertoFiscal:Modelos.
        //
        // Existe porque el modelo NO se puede deducir del puerto: con el driver generico de
        // Windows (usbser.sys) el dispositivo se llama "Dispositivo serie USB" y el fabricante es
        // "Microsoft" — medido sobre una HKA80 conectada, 2026-09-10. El puente CDC no publica el
        // modelo. Asi que el nombre se declara acá y se ata al hardware que lo delata.
        //
        // Es la misma solucion que el impfis legacy, que usa un archivo vacio `hka80.on` como
        // bandera de modelo — solo que centralizada y legible.
        public class ModeloFiscalConfig
        {
            public string Nombre { get; set; }
            // VID/PID del puente USB-serie. Es el identificador que aguanta: no depende del texto
            // del driver y sobrevive a que Windows reasigne el numero de COM.
            public List<string> IdsHardware { get; set; } = new List<string>();
            // Textos en la descripcion o el fabricante. Solo sirven si el dispositivo trae un
            // driver propio que se identifica.
            public List<string> Firmas { get; set; } = new List<string>();
        }

        public class PuertoFiscalInfo
        {
            public string Puerto { get; set; }
            // Nombre del modelo detectado (ej. "HKA80"), o null. Ver ModeloFiscalConfig: es el
            // nombre configurado para ese hardware, no uno que declare la impresora.
            public string ImpresoraFiscal { get; set; }
            // Como se decidio. Valores: "id-hardware" (coincidio el VID/PID del puente
            // USB-serie), "firma" (coincidio un texto de la descripcion o el fabricante),
            // "unico-puerto" (no coincidio nada, pero hay un solo COM en la maquina),
            // "ambiguo" (varios COM y ninguno decidible), "sin-puertos" (no hay ninguno).
            public string Origen { get; set; }
            // En castellano y para mostrar: por que dio ese resultado.
            public string Detalle { get; set; }
            public List<PuertoSerieInfo> Candidatos { get; set; } = new List<PuertoSerieInfo>();
        }
    }
}
