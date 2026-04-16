using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


namespace SalesforceDynamicsGPIntegration
{
    public class GpDataSync
    {

        public string salesRepId { get; set; }
        public string salesRepName { get; set; }
        public string productCode { get; set; }
        public string productId { get; set; }
        public string productName { get; set; }

        public string accountNumber { get; set; }

        public string accountName { get; set; }

        public long salesDate { get; set; }

        public decimal quantity { get; set; }

        public decimal amount { get; set; }

        public string invoiceNumber { get; set; }

        public int sopType{ get; set; } = 3;

        public long lineItemSequence { get; set; }  =0;     

        public long componentSequence { get; set; }   =0;

        public string productClassCode { get; set; }
        public string productFamily { get; set; }

        public string billingCity { get; set; }
        public string shippingCity { get; set; }
        public string shippingState { get; set; }
        public string shippingZipCode { get; set; }

    }
}