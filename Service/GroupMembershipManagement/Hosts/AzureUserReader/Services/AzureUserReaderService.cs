// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs.Models;
using Hosts.AzureUserReader;
using Models;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using Services.Entities.CustomExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class AzureUserReaderService : IAzureUserReaderService
    {
        private const string MemberIdsFileName = "memberids.csv";

        private readonly IBlobClientFactory _blobClientFactory;
        private readonly ILogger<AzureUserReaderService> _logger;
        private readonly IStorageAccountSecret _storageAccountSecret;

        public AzureUserReaderService(
            IStorageAccountSecret storageAccountSecret,
            ILogger<AzureUserReaderService> logger,
            IBlobClientFactory blobClientFactory)
        {
            _storageAccountSecret = storageAccountSecret ?? throw new ArgumentNullException(nameof(storageAccountSecret));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobClientFactory = blobClientFactory ?? throw new ArgumentNullException(nameof(blobClientFactory));
        }

        public async Task<IList<string>> GetPersonnelNumbersAsync(string containerName, string blobPath)
        {
            var uri = new Uri($"https://{_storageAccountSecret.AccountName}.blob.core.windows.net/{containerName}/{blobPath}");
            var blob = await DownloadFileAsync(uri);
            var personnelNumbers = ExtractPersonnelNumbers(blob).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            _logger.PersonnelNumbersRetrieved(personnelNumbers.Count);

            return personnelNumbers;
        }

        public async Task UploadUsersMemberIdAsync(UploadRequest request)
        {
            var blobPath = $"{request.BlobTargetDirectory}/{MemberIdsFileName}";
            var usersRetrieved = request.Users.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();

            var uri = new Uri($"https://{_storageAccountSecret.AccountName}.blob.core.windows.net/{request.ContainerName}/{blobPath}");
            await UploadFileAsync(uri, usersRetrieved);

            _logger.UserIdsUploaded(usersRetrieved.Count);
        }

        private async Task<Stream> DownloadFileAsync(Uri blobPath)
        {
            int status;
            Response<BlobDownloadInfo> response = null;
            var blobClient = _blobClientFactory.GetBlobClient(blobPath);

            if (blobClient.Exists())
            {
                response = await blobClient.DownloadAsync();
                status = response.GetRawResponse().Status;
            }
            else
            {
                _logger.FileNotFound(blobPath);
                throw new FileNotFoundException($"File not found {blobPath}.");
            }

            if (status >= 200 && status <= 299)
            {
                return response.Value.Content;
            }
            else
            {
                var rawResponse = response.GetRawResponse();
                _logger.FileDownloadError(rawResponse.Status, rawResponse.ReasonPhrase);
                throw new DownloadFileException($"An error occurred while downloading the file.\nStatusCode:{rawResponse.Status}\n{rawResponse.ReasonPhrase}");
            }
        }

        private async Task<Stream> DownloadFileIfExistsAsync(Uri blobPath)
        {
            Response<BlobDownloadInfo> response = null;
            var blobClient = _blobClientFactory.GetBlobClient(blobPath);

            if (blobClient.Exists())
            {
                response = await blobClient.DownloadAsync();
                return response.Value.Content;
            }

            return new MemoryStream();
        }

        private List<string> ExtractFileContent(Stream stream, bool skipHeader)
        {
            var lines = new List<string>();
            using (var reader = new StreamReader(stream))
            {
                if (skipHeader)
                    reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line.Replace("\"", string.Empty).Trim());
                }
            }

            return lines;
        }

        private List<string> ExtractPersonnelNumbers(Stream stream)
        {
            return ExtractFileContent(stream, true).Distinct().ToList();
        }

        private List<GraphProfileInformation> ExtractUsers(Stream stream)
        {
            var lines = ExtractFileContent(stream, true).Distinct().ToList();
            var users = new List<GraphProfileInformation>();

            foreach (var line in lines)
            {
                var data = line.Split(",", StringSplitOptions.RemoveEmptyEntries);
                if (data.Length >= 3)
                    users.Add(new GraphProfileInformation { PersonnelNumber = data[0], Id = data[1], UserPrincipalName = data[2] });
            }

            return users;
        }

        private async Task UploadFileAsync(Uri blobPath, List<GraphProfileInformation> users)
        {
            var blobClient = _blobClientFactory.GetBlobClient(blobPath);
            using var st = new StreamWriter(new MemoryStream());
            st.WriteLine("PersonnelNumber,AzureObjectId,UserPrincipalName");
            foreach (var user in users)
            {
                st.WriteLine($"{user.PersonnelNumber},{user.Id},{user.UserPrincipalName}");
            }

            await st.FlushAsync();
            st.BaseStream.Position = 0;

            await blobClient.UploadAsync(st.BaseStream, true);
        }
    }
}