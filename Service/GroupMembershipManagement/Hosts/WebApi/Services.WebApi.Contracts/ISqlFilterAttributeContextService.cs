// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    public interface ISqlFilterAttributeContextService
    {
        /// <summary>
        /// Builds a prompt fragment describing the attributes referenced by the supplied SQL filters:
        /// the admin-configured friendly label for each attribute and, for coded attributes, the
        /// description of every code literal used by the filters.
        /// Returns an empty string when no context can be resolved.
        /// </summary>
        Task<string> BuildAttributeContextAsync(IEnumerable<string?> filters);
    }
}
