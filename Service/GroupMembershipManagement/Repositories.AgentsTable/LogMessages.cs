// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;

namespace Repositories.AgentsTable
{
    // 260070-260079: the agents table repository. AgentReader's host messages use 260000-260069.
    public static partial class LogMessages
    {
        [LoggerMessage(EventId = 260070, Level = LogLevel.Information,
            Message = "Agent write confirmed {RowCount} rows, including {PreviouslyWritten} matching rows from activity redelivery")]
        public static partial void WriteConfirmed(this ILogger logger, int rowCount, int previouslyWritten);
    }
}
