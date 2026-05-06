// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetMembershipDownloadHandler : RequestHandlerBase<GetMembershipDownloadRequest, GetMembershipDownloadResponse>
    {
        private readonly ILogger<GetMembershipDownloadHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public GetMembershipDownloadHandler(
            ILogger<GetMembershipDownloadHandler> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IBlobStorageRepository blobStorageRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                var zipBytes = CreateZipArchive(jsonBytes, groupId, request.RunId);

                response.FileContent = zipBytes;
                response.FileName = $"membership_changes_{groupId}_{request.RunId}.zip";
                response.StatusCode = HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                _logger.MembershipDownloadFailed(request.SyncJobId, request.RunId, ex);
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

        private static byte[] CreateZipArchive(byte[] jsonBytes, string groupId, Guid runId)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                var jsonEntry = archive.CreateEntry($"membership_changes_{groupId}_{runId}.json", CompressionLevel.Optimal);
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
