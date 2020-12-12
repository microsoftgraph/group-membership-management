using System;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class LogMessage
    {
        public Guid? InstanceId { get; set; }
        public string MessageTypeName { get; set; }
        public Guid? RunId { get; set; }
        public string Message { get; set; }
    }
}
