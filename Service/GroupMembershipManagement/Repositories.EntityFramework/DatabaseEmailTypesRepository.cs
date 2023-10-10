// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class DatabaseEmailTypesRepository : IDatabaseEmailTypesRepository
    {
        private readonly GMMContext _context;

        public DatabaseEmailTypesRepository(GMMContext gmmContext)
        {
            _context = gmmContext ?? throw new ArgumentNullException(nameof(gmmContext));
        }

        public async Task<int?> GetEmailTypeIdByEmailTemplateName(string emailTemplateName)
        {
            var emailType = await _context.EmailTypes
                .FirstOrDefaultAsync(e => e.EmailContentTemplateName == emailTemplateName);
                
            return emailType?.EmailTypeId;
        }

    }
}