using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


namespace SalesforceDynamicsGPIntegration
{
    public class GpDataSyncRequest
    {

        public string salesRepId { get; set; }
        public string productCode { get; set; }

        public string accountNumber { get; set; }

        public long salesDate { get; set; }

        public decimal quantity { get; set; }

        public decimal amount { get; set; }

        public string invoiceNumber { get; set; }

        public int sopType{ get; set; } = 3;

        public long lineItemSequence { get; set; }  =0;     

        public long componentSequence { get; set; }   =0;

        public string productClassCode { get; set; }

    }
}