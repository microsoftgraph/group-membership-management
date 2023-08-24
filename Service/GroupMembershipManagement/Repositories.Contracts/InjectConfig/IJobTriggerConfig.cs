// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IJobTriggerConfig
    {
        public bool GMMHasGroupReadWriteAllPermissions { get; }
		public int JobCountThreshold { get; }
		public int JobPercentThreshold { get; }

	}
}
