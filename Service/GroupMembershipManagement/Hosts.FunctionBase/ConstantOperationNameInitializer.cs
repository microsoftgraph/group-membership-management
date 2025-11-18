// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Hosts.FunctionBase
{
    public sealed class ConstantOperationNameInitializer : ITelemetryInitializer
    {
        private readonly string _name;
        public ConstantOperationNameInitializer(string name) => _name = name;

        public void Initialize(ITelemetry telemetry)
        {
            // Set once; if something (like an Activity) already provided a more specific name, keep it.
            if (string.IsNullOrEmpty(telemetry.Context.Operation.Name))
            {
                telemetry.Context.Operation.Name = _name;
            }
        }
    }
}
