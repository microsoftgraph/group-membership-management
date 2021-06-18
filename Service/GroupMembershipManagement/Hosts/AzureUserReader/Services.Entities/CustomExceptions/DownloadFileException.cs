// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Services.Entities.CustomExceptions
{
    public class DownloadFileException : Exception
    {
        public DownloadFileException() : base()
        {
        }

        public DownloadFileException(string message) : base(message)
        {
        }

        public DownloadFileException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
