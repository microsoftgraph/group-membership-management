// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace Models.ServiceBus
{
    public class ServiceBusMessage
    {
        public string MessageId { get; set; }

        /// <summary>
        /// Gets or sets the body of the message.
        /// </summary>
        /// <remarks>
        /// The easiest way to create a new message from a string is the following:
        /// <code>
        /// message.Body = System.Text.Encoding.UTF8.GetBytes("MessageBody");
        /// </code>
        /// </remarks>
        public byte[] Body { get; set; }

        /// <summary>
        /// Gets the "user properties" bag, which can be used for custom message metadata.
        /// </summary>
        /// <remarks>
        /// Only following value types are supported:
        /// byte, sbyte, char, short, ushort, int, uint, long, ulong, float, double, decimal,
        /// bool, Guid, string, Uri, DateTime, DateTimeOffset, TimeSpan
        /// </remarks>
        public Dictionary<string, object> UserProperties { get; set; } = new Dictionary<string, object>();

    }
}
