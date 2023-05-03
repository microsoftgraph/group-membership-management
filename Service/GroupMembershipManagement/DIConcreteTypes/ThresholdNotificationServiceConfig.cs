// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace DIConcreteTypes
{
    public class ThresholdNotificationServiceConfig
    {
        public string ApiHostname { get; set; }
        public Guid ActionableEmailProviderId { get; set; }
    }
}
