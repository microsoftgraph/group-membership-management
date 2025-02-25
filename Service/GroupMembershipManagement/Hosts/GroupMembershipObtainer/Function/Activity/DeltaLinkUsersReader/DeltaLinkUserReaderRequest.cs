// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
	public class DeltaLinkUserReaderRequest
	{
		public Guid RunId { get; set; }
		public string DeltaLink { get; set; }
	}
}