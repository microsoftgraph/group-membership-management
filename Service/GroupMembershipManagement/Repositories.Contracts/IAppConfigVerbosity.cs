// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    public enum VerbosityLevel
    {
        LOW = 1,
        HIGH = 2
    }

    public interface IAppConfigVerbosity
    {
        public VerbosityLevel Verbosity { get; }
	}
}
