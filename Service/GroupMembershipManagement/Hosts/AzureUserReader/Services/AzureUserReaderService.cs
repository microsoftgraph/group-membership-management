// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Storage.Blobs.Models;
using Entities;
using Models;
using Repositories.Contracts;
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

        private readonly IBlobClientFactory _blobClientFactory = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IStorageAccountSecret _storageAccountSecret = null;

        public AzureUserReaderService(
            IStorageAccountSecret storageAccountSecret,
            ILoggingRepository loggingRepository,
            IBlobClientFactory blobClientFactory)
        {
            _storageAccountSecret = storageAccountSecret ?? throw new ArgumentNullException(nameof(storageAccountSecret));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobClientFactory = blobClientFactory ?? throw new ArgumentNullException(nameof(blobClientFactory));
        }

        public async Task<IList<string>> GetPersonnelNumbersAsync(string containerName, string blobPath)
        {
            var blob = await DownloadFileAsync(_storageAccountSecret.ConnectionString, containerName, blobPath);
            var personnelNumbers = ExtractPersonnelNumbers(blob);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved {personnelNumbers.Count} personnel numbers." });

            return personnelNumbers;
        }

        public async Task UploadUsersMemberIdAsync(UploadRequest request)
        {
            var blobPath = $"{request.BlobTargetDirectory}/{MemberIdsFileName}";
            var file = await DownloadFileIfExistsAsync(_storageAccountSecret.ConnectionString, request.ContainerName, blobPath);
            var users = ExtractUsers(file);
            var existingUserIds = new HashSet<string>(users.Select(x => x.PersonnelNumber));
            var newUsers = request.Users.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();

            foreach (var user in newUsers)
            {
                if (!existingUserIds.Contains(user.PersonnelNumber))
                    users.Add(user);
            }

            await UploadFileAsync(_storageAccountSecret.ConnectionString, request.ContainerName, blobPath, users);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Uploaded {newUsers.Count} new user ids." });
        }

        private async Task<Stream> DownloadFileAsync(string connectionString, string containerName, string filePath)
        {
            int status;
            Response<BlobDownloadInfo> response = null;
            var blobClient = _blobClientFactory.GetBlobClient(connectionString, containerName, filePath);

            if (blobClient.Exists())
            {
                response = await blobClient.DownloadAsync();
                status = response.GetRawResponse().Status;
            }
            else
            {
                var message = $"File not found {filePath}.";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = message });
                throw new FileNotFoundException(message);
            }

            if (status >= 200 && status <= 299)
            {
                return response.Value.Content;
            }
            else
            {
                var rawResponse = response.GetRawResponse();
                var message = $"An error occurred while downloading the file.\nStatusCode:{rawResponse.Status}\n{rawResponse.ReasonPhrase}";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = message });
                throw new DownloadFileException(message);
            }
        }

        private async Task<Stream> DownloadFileIfExistsAsync(string connectionString, string containerName, string filePath)
        {
            Response<BlobDownloadInfo> response = null;
            var blobClient = _blobClientFactory.GetBlobClient(connectionString, containerName, filePath);

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

        private async Task UploadFileAsync(string connectionString, string containerName, string filePath, List<GraphProfileInformation> users)
        {
            var blobClient = _blobClientFactory.GetBlobClient(connectionString, containerName, filePath);
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