// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class FileDownloaderRequest : GraphUpdaterRequestBase
    {
        public string FilePath { get; set; }
    }
}
