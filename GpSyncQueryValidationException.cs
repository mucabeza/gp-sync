using System;

namespace SalesforceDynamicsGPIntegration
{
    public class GpSyncQueryValidationException : Exception
    {
        public GpSyncQueryValidationException(string message) : base(message)
        {
        }

        public GpSyncQueryValidationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
