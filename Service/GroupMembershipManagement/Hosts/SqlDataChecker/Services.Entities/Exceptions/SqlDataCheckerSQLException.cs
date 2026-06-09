// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Services.Entities
{
	[ExcludeFromCodeCoverage]
	public class SqlDataCheckerSQLException : Exception
	{        
        public SqlDataCheckerSQLException(string message, Exception inner) : base(message, inner)
        {           
        }
    }
}
