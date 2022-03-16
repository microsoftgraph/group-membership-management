// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
	[ExcludeFromCodeCoverage]
	public class GraphUpdaterSessionLockLostException: Exception
	{
        public GraphUpdaterSessionLockLostException(string message, Exception inner) 
            : base(message, inner)
        {
        }
    }
}
