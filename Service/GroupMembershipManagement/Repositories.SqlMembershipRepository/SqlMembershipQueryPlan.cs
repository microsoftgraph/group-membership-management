// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Repositories.SqlMembershipRepository
{
    /// <summary>
    /// Carries generated SQL plus the flags the repository needs before reading a membership result.
    /// </summary>
    internal sealed class SqlMembershipQueryPlan
    {
        /// <summary>
        /// Creates a query plan for a generated SQL membership command.
        /// </summary>
        /// <param name="commandText">
        /// The SQL command text to execute. Null, empty, or whitespace throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="requiresAgents">
        /// True when the command reads from the agents table, either because agents are emitted or because the filter
        /// references agents columns. False means the command does not touch the agents table, so a missing agents table
        /// is not an error.
        /// </param>
        /// <param name="returnsAgentColumns">
        /// True when the command projection includes the <c>AgentObjectId</c> and <c>HasAgentRow</c> columns. Omit it
        /// for commands, such as probes, that do not return those columns.
        /// </param>
        public SqlMembershipQueryPlan(
            string commandText,
            bool requiresAgents,
            bool returnsAgentColumns = false)
        {
            CommandText = string.IsNullOrWhiteSpace(commandText)
                ? throw new ArgumentException("Command text is required.", nameof(commandText))
                : commandText;
            RequiresAgents = requiresAgents;
            ReturnsAgentColumns = returnsAgentColumns;
        }

        /// <summary>
        /// Gets the SQL command text the repository should execute.
        /// </summary>
        public string CommandText { get; }

        /// <summary>
        /// Gets whether the command reads the matching agents table. When true, that table must exist, be readable by
        /// the caller, and have the <c>AgentObjectId</c> and <c>AgentManagerId</c> columns.
        /// </summary>
        public bool RequiresAgents { get; }

        /// <summary>
        /// Gets whether the evaluation result set includes <c>AgentObjectId</c> (ordinal 2) and <c>HasAgentRow</c>
        /// (ordinal 3) after the user columns. <c>HasAgentRow</c> is false when the user has no matching agent row.
        /// </summary>
        public bool ReturnsAgentColumns { get; }
    }
}
