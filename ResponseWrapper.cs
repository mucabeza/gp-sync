using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SalesforceDynamicsGPIntegration
{
    public class ResponseWrapper
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; } = false;

        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty; 

        [JsonPropertyName("errors")]       
        public List<ResponseErrorDetails> Errors { get; set; } = new List<ResponseErrorDetails>();
    }

    public class ResponseErrorDetails
    {
        [ JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;

        [JsonPropertyName("invoiceNumber")]
         public string InvoiceNumber { get; set; }

        [JsonPropertyName("sopType")]
        public int SopType{ get; set; } = 3;

        [JsonPropertyName("lineItemSequence")]
        public long LineItemSequence { get; set; }  =0;     

        [JsonPropertyName("componentSequence")]
        public long ComponentSequence { get; set; }   =0;
    }
}