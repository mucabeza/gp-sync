using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SalesforceDynamicsGPIntegration
{
    public class GPRequestSync
    {
        public List<GpDataSync> gpData {get; set;} = new List<GpDataSync>();
        public bool isLastOne {get; set;} = false;
        public string filterRecordId {get; set;} = String.Empty;

    }
}