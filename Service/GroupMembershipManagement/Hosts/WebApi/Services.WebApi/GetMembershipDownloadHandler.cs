// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.IO.Compression;
using System.Net;

namespace Services
{
    public class GetMembershipDownloadHandler : RequestHandlerBase<GetMembershipDownloadRequest, GetMembershipDownloadResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public GetMembershipDownloadHandler(
            ILoggingRepository loggingRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IBlobStorageRepository blobStorageRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        protected override async Task<GetMembershipDownloadResponse> ExecuteCoreAsync(GetMembershipDownloadRequest request)
        {
            var response = new GetMembershipDownloadResponse();

            try
            {
                var (syncJob, statusCode) = await GetAuthorizedSyncJobAsync(request.SyncJobId);
                if (syncJob == null)
                {
                    response.StatusCode = statusCode;
                    return response;
                }

                var groupId = syncJob.TargetOfficeGroupId.ToString();
                var blobResult = await _blobStorageRepository.FindAggregatedFileByRunIdAsync(groupId, request.RunId.ToString());
                if (blobResult.BlobStatus == BlobStatus.NotFound)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                var fileContent = await _blobStorageRepository.DownloadFileAsync(blobResult.Path);
                if (fileContent.BlobStatus == BlobStatus.NotFound)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                var membershipJson = TryDecompress(fileContent.Content);
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(membershipJson);
                var zipBytes = CreateZipArchive(jsonBytes, request.RunId);

                response.FileContent = zipBytes;
                response.FileName = $"membership_changes_{request.RunId}.zip";
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error downloading membership data for SyncJobId={request.SyncJobId}, RunId={request.RunId}: {ex.Message}",
                });
                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        private static string TryDecompress(string content)
        {
            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        private static byte[] CreateZipArchive(byte[] jsonBytes, Guid runId)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                var jsonEntry = archive.CreateEntry($"membership_changes_{runId}.json", CompressionLevel.Optimal);
                using (var entryStream = jsonEntry.Open())
                {
                    entryStream.Write(jsonBytes, 0, jsonBytes.Length);
                }
            }

            return memoryStream.ToArray();
        }

        private async Task<(SyncJob? syncJob, HttpStatusCode statusCode)> GetAuthorizedSyncJobAsync(Guid syncJobId)
        {
            var job = await _databaseSyncJobsRepository.GetSyncJobAsync(syncJobId);
            return job != null ? (job, HttpStatusCode.OK) : (null, HttpStatusCode.NotFound);
        }
    }
}
