// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;

namespace Models.Entities
{
    [ExcludeFromCodeCoverage]
    public class EmailMessage
    {
        public string Subject { get; set; }
        public string Content { get; set; }
        public string SenderAddress { get; set; }
        public string SenderPassword { get; set; }
        public string ToEmailAddresses { get; set; }
        public string CcEmailAddresses { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public string[] AdditionalSubjectParams { get; set; }
    }
}
