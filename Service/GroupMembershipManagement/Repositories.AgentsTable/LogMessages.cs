// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;

namespace Repositories.AgentsTable
{
    public static partial class LogMessages
    {
        [LoggerMessage(EventId = 260061, Level = LogLevel.Information,
            Message = "Agent write confirmed {RowCount} rows, including {PreviouslyWritten} matching rows from activity redelivery")]
        public static partial void WriteConfirmed(this ILogger logger, int rowCount, int previouslyWritten);
    }
}
