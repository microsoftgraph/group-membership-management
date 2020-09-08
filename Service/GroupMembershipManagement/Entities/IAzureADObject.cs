// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Entities
{
	public interface IAzureADObject
	{
		Guid ObjectId { get; }
	}
}

