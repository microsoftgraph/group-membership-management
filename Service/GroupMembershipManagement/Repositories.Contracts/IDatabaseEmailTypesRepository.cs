// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IEmailTypesRepository
    {
        Task<int?> GetEmailTypeIdByEmailTemplateName(string emailTemplateName);

    }
}       

