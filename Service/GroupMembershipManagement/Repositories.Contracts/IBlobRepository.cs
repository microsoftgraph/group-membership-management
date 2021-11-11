// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IBlobRepository
    {
        public Task<string> DownloadFileAndGetContent(string filePath);
        public Task ForceUploadFileContent(string filePath, string content);
    }
}
