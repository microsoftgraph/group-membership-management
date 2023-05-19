// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;

namespace DIConcreteTypes
{
    public class AppConfigVerbosity: IAppConfigVerbosity
    {
        public VerbosityLevel Verbosity { get; set; }

        public AppConfigVerbosity(VerbosityLevel verbosityLevel)
        {
            Verbosity = verbosityLevel;
        }

        public AppConfigVerbosity()
        {
        }
    }
}
