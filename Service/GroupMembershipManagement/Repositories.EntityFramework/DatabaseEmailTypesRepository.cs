// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework
{
    public class EmailTypesRepository : IEmailTypesRepository
    {
        private readonly GMMContext _writeContext;
        private readonly GMMReadContext _readContext;

        public EmailTypesRepository(GMMContext writeContext, GMMReadContext readContext)
        {
            _writeContext = writeContext ?? throw new ArgumentNullException(nameof(writeContext));
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<int?> GetEmailTypeIdByEmailTemplateName(string emailTemplateName)
        {
            var emailType = await _readContext.EmailTypes
                .FirstOrDefaultAsync(e => e.EmailContentTemplateName == emailTemplateName);
                
            return emailType?.EmailTypeId;
        }

    }
}