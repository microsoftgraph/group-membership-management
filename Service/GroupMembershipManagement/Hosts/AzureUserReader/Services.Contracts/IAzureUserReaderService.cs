// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IAzureUserReaderService
    {
        public Task<IList<string>> GetPersonnelNumbersAsync(string containerName, string blobPath);

        public Task UploadUsersMemberIdAsync(UploadRequest request);
    }
}