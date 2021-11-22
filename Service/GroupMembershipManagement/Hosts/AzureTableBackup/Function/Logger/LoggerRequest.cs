using System;

namespace Hosts.AzureTableBackup
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public Guid RunId { get; set; }
    }
}
