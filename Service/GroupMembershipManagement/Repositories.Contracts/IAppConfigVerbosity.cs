// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    public enum VerbosityLevel
    {
        INFO = 1,
        DEBUG = 2
    }

    public interface IAppConfigVerbosity
    {
        public VerbosityLevel Verbosity { get; }
    }
}
