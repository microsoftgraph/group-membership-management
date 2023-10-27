// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Polly;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockDatabaseEmailTypesRepository : IEmailTypesRepository
    {

        private readonly Dictionary<string, int?> _emailTemplateToIdMapping;

        public MockDatabaseEmailTypesRepository(Dictionary<string, int?> emailTemplateToIdMapping = null)
        {
            _emailTemplateToIdMapping = emailTemplateToIdMapping ?? new Dictionary<string, int?>();
        }
        public async Task<int?> GetEmailTypeIdByEmailTemplateName(string emailTemplateName)
        {
            if (_emailTemplateToIdMapping.TryGetValue(emailTemplateName, out int? emailTypeId))
            {
                return await Task.FromResult(emailTypeId);
            }
            return null;
        }

    }
}