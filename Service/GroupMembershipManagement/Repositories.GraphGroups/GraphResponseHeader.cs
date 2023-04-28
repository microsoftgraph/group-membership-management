// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.GraphGroups
{
    internal static class GraphResponseHeader
    {
        public const string ResourceUnitHeader = "x-ms-resource-unit";
        public const string ThrottlePercentageHeader = "x-ms-throttle-limit-percentage";
        public const string ThrottleScopeHeader = "x-ms-throttle-scope";
        public const string ThrottleInfoHeader = "x-ms-throttle-information";
    }
}
