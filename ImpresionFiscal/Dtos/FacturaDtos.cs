using System.Text.Json.Serialization;

namespace ImpresionFiscal.Dtos
{
    public class FacturaDtos
    {
        public class FacturaFiscal
        {
            [JsonPropertyName("cliente")]
            public ClienteFactFiscal cliente { get; set; } = new();

            [JsonPropertyName("items")]
            public List<ItemsFactFiscal> items { get; set; } = new();

            [JsonPropertyName("forma_pagos")]
            public List<FormasPagos> formas_pago { get; set; } = new();

            [JsonPropertyName("puerto")]
            public string puerto { get; set; } = string.Empty;

            [JsonPropertyName("fact_num")]
            public string fact_num { get; set; } = string.Empty;

            [JsonPropertyName("igtf")]
            public bool? igtf { get; set; }
        }

        public class ClienteFactFiscal
        {
            [JsonPropertyName("cli_des")]
            public string cli_des { get; set; } = string.Empty;

            [JsonPropertyName("rif_cli")]
            public string rif_cli { get; set; } = string.Empty;

            [JsonPropertyName("direc_cli")]
            public string direc_cli { get; set; } = string.Empty;

            [JsonPropertyName("tlf_cli")]
            public string tlf_cli { get; set; } = string.Empty;
        }

        public class ItemsFactFiscal
        {
            [JsonPropertyName("co_art")]
            public string co_art { get; set; } = string.Empty;

            [JsonPropertyName("des_art")]
            public string des_art { get; set; } = string.Empty;

            [JsonPropertyName("cantidad")]
            public decimal cantidad { get; set; }

            [JsonPropertyName("precio")]
            public decimal precio { get; set; }

            [JsonPropertyName("tipo_iva")]
            public int tipo_iva { get; set; }

            [JsonPropertyName("porc_desc")]
            public string porc_desc { get; set; } = string.Empty;
        }

        public class FormasPagos
        {
            [JsonPropertyName("des_fb")]
            public string? DesFb { get; set; }

            [JsonPropertyName("tot_bs")]
            public decimal TotBs { get; set; }

            [JsonPropertyName("cod_fis")]
            public string? CodFis { get; set; }
        }

        public class CabeceraNcr
        {
            [JsonPropertyName("empresa")]
            public string Empresa { get; set; } = string.Empty;

            [JsonPropertyName("tipo_doc")]
            public string TipoDoc { get; set; } = string.Empty;

            [JsonPropertyName("num_doc")]
            public string NumDoc { get; set; } = string.Empty;

            [JsonPropertyName("num_ped")]
            public string NumPed { get; set; } = string.Empty;

            [JsonPropertyName("co_cli")]
            public string CoCli { get; set; } = string.Empty;

            [JsonPropertyName("des_cli")]
            public string DesCli { get; set; } = string.Empty;

            [JsonPropertyName("email")]
            public string Email { get; set; } = string.Empty;

            [JsonPropertyName("co_ven")]
            public string CoVen { get; set; } = string.Empty;

            [JsonPropertyName("ven_des")]
            public string VenDes { get; set; } = string.Empty;

            [JsonPropertyName("fec_emis")]
            public DateTime FecEmis { get; set; }

            [JsonPropertyName("fec_venc")]
            public DateTime FecVenc { get; set; }

            [JsonPropertyName("tasa")]
            public decimal Tasa { get; set; }

            [JsonPropertyName("desc_par")]
            public decimal DescPar { get; set; }

            [JsonPropertyName("co_cond")]
            public string CoCond { get; set; } = string.Empty;

            [JsonPropertyName("cond_des")]
            public string CondDes { get; set; } = string.Empty;

            [JsonPropertyName("co_tran")]
            public string CoTran { get; set; } = string.Empty;

            [JsonPropertyName("des_tran")]
            public string DesTran { get; set; } = string.Empty;

            [JsonPropertyName("total_items")]
            public decimal TotalItems { get; set; }

            [JsonPropertyName("total_usd")]
            public decimal TotalUsd { get; set; }

            [JsonPropertyName("cod_dev")]
            public string? CodDev { get; set; }

            [JsonPropertyName("tlf_cli")]
            public string? TlfCli { get; set; }

            [JsonPropertyName("direc_cli")]
            public string? DirecCli { get; set; }
            [JsonPropertyName("serial")]
            public string? Serial { get; set; }

            [JsonPropertyName("puerto_fiscal")]
            public string? PuertoFiscal { get; set; }

            [JsonPropertyName("igtf")]
            public bool? igtf { get; set; }
        }

        public class DetalleNcr
        {
            [JsonPropertyName("co_art")]
            public string CoArt { get; set; } = string.Empty;

            [JsonPropertyName("des_art")]
            public string DesArt { get; set; } = string.Empty;

            [JsonPropertyName("reng_num")]
            public int RengNum { get; set; }

            [JsonPropertyName("co_alma")]
            public string CoAlma { get; set; } = string.Empty;

            [JsonPropertyName("total_art")]
            public decimal TotalArt { get; set; }

            [JsonPropertyName("tasa")]
            public decimal Tasa { get; set; }

            [JsonPropertyName("desc_par")]
            public decimal DescPar { get; set; }

            [JsonPropertyName("desc_vol1")]
            public decimal DescVol1 { get; set; }

            [JsonPropertyName("desc_vol2")]
            public decimal DescVol2 { get; set; }

            [JsonPropertyName("iva")]
            public decimal Iva { get; set; }

            [JsonPropertyName("precio_bs")]
            public decimal PrecioBs { get; set; }

            [JsonPropertyName("precio_usd")]
            public decimal PrecioUsd { get; set; }

            [JsonPropertyName("porc_desc")]
            public string PorcDesc { get; set; } = string.Empty;

            [JsonPropertyName("cos_pro_un")]
            public decimal CosProUn { get; set; }

            [JsonPropertyName("cos_pro_om")]
            public decimal CosProOm { get; set; }

            [JsonPropertyName("ult_cos_un")]
            public decimal UltCosUn { get; set; }

            [JsonPropertyName("ult_cos_om")]
            public decimal UltCosOm { get; set; }

            [JsonPropertyName("apto_para_venta")]
            public bool AptoParaVenta { get; set; }

            [JsonPropertyName("total_devolver")]
            public decimal TotalDevolver { get; set; }

            [JsonPropertyName("uni_venta")]
            public string UniVenta { get; set; } = string.Empty;

            [JsonPropertyName("reng_neto_bs")]
            public decimal RengNetoBs { get; set; }

            [JsonPropertyName("reng_neto_usd")]
            public decimal RengNetoUsd { get; set; }

            [JsonPropertyName("tipo_imp")]
            public int TipoImp { get; set; }

            [JsonPropertyName("equivalencia")]
            public decimal Equivalencia { get; set; }

            [JsonPropertyName("uni_min")]
            public decimal UniMin { get; set; }

            [JsonPropertyName("u_muestra")]
            public decimal UMuestra { get; set; }
        }

        public class NotaCredito
        {
            [JsonPropertyName("cabecera")]
            public CabeceraNcr Cabecera { get; set; } = new();

            [JsonPropertyName("detalles")]
            public List<DetalleNcr> Detalles { get; set; } = new();
        }

        public class RegistrosCierresValidarZ
        {
            [JsonPropertyName("id")]
            public int? Id { get; set; }
            [JsonPropertyName("empresa")]
            public string Empresa { get; set; } = string.Empty;

            [JsonPropertyName("numeroZ")]
            public string NumeroZ { get; set; } = string.Empty;

            [JsonPropertyName("serial")]
            public string Serial { get; set; } = string.Empty;

            [JsonPropertyName("des_estacion")]
            public string DesEstacion { get; set; } = string.Empty;

            [JsonPropertyName("fechaCierre")]
            public DateTime FechaCierre { get; set; }

            [JsonPropertyName("fechaRegistro")]
            public DateTime? FechaRegistro { get; set; }

            [JsonPropertyName("fueReconciliado")]
            public bool? FueReconciliado { get; set; }

            [JsonPropertyName("co_us_in")]
            public string CoUsIn { get; set; } = string.Empty;

            [JsonPropertyName("id_cajero")]
            public int IdCajero { get; set; }

            [JsonPropertyName("puerto_fiscal")]
            public int PuertoFiscal { get; set; }
        }
    }
}
