// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using Services.WebApi.Validators;
using System.Net;
using WebApi.Models.DTOs;
using SyncJob = Models.SyncJob;

namespace Services.WebApi
{
    public class PatchJobHandler : RequestHandlerBase<PatchJobRequest, PatchJobResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;

        public PatchJobHandler(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        protected override async Task<PatchJobResponse> ExecuteCoreAsync(PatchJobRequest request)
        {
            var response = new PatchJobResponse();

            var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
            if (syncJob == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var isGroupOwner = await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(request.UserIdentity, syncJob.TargetOfficeGroupId);
            if (!(isGroupOwner || request.IsAllowed))
            {
                response.StatusCode = HttpStatusCode.Forbidden;
                return response;
            }

            var syncJobPatch = MapEntityToDto(request.SyncJobId, syncJob);
            request.PatchDocument.ApplyTo(syncJobPatch);

            var validationResult = Validate(syncJobPatch);
            if (!validationResult.IsValid)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.ErrorCode = validationResult.ErrorCode;
                return response;
            }

            if (syncJob.Status == SyncStatus.InProgress.ToString() || syncJob.Status == SyncStatus.StuckInProgress.ToString())
            {
                response.StatusCode = HttpStatusCode.PreconditionFailed;
                response.ErrorCode = "JobInProgress";
                return response;
            }

            var updatedSyncJob = MapDtoToEntity(syncJob, syncJobPatch, request.SyncJobId);
            await _databaseSyncJobsRepository.UpdateSyncJobsAsync(new[] { updatedSyncJob });

            return response;
        }

        private ValidationResponse Validate(SyncJobPatch syncJobPatch)
        {
            var validators = new List<IValidator<SyncJobPatch>>
            {
                new StatusValidator()
            };

            var isValid = true;
            var errors = new List<string>();

            foreach (var validator in validators)
            {
                var validationResult = validator.Validate(syncJobPatch);
                if (!validationResult.IsValid)
                {
                    isValid = false;
                    errors.Add(validationResult.ErrorCode ?? string.Empty);
                }
            }

            return new ValidationResponse
            {
                IsValid = isValid,
                ErrorCode = string.Join("\n", errors)
            };
        }

        private SyncJobPatch MapEntityToDto(Guid syncJobId, SyncJob syncJob)
        {
            return new SyncJobPatch
            {
                RunId = syncJob.RunId,
                Requestor = syncJob.Requestor,
                TargetOfficeGroupId = syncJob.TargetOfficeGroupId,
                Destination = syncJob.Destination,
                AllowEmptyDestination = syncJob.AllowEmptyDestination,
                Status = syncJob.Status,
                LastRunTime = syncJob.LastRunTime,
                LastSuccessfulRunTime = syncJob.LastSuccessfulRunTime,
                LastSuccessfulStartTime = syncJob.LastSuccessfulStartTime,
                Period = syncJob.Period,
                Query = syncJob.Query,
                StartDate = syncJob.StartDate,
                IgnoreThresholdOnce = syncJob.IgnoreThresholdOnce,
                ThresholdPercentageForAdditions = syncJob.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = syncJob.ThresholdPercentageForRemovals,
                IsDryRunEnabled = syncJob.IsDryRunEnabled,
                DryRunTimeStamp = syncJob.DryRunTimeStamp,
                ThresholdViolations = syncJob.ThresholdViolations,
            };
        }

        private SyncJob MapDtoToEntity(SyncJob syncJob, SyncJobPatch syncJobPatch, Guid syncJobId)
        {
            syncJob.Id = syncJobId;
            syncJob.RunId = syncJobPatch.RunId;
            syncJob.Requestor = syncJobPatch.Requestor;
            syncJob.TargetOfficeGroupId = syncJobPatch.TargetOfficeGroupId;
            syncJob.Destination = syncJobPatch.Destination;
            syncJob.AllowEmptyDestination = syncJobPatch.AllowEmptyDestination;
            syncJob.Status = syncJobPatch.Status;
            syncJob.LastRunTime = syncJobPatch.LastRunTime;
            syncJob.LastSuccessfulRunTime = syncJobPatch.LastSuccessfulRunTime;
            syncJob.LastSuccessfulStartTime = syncJobPatch.LastSuccessfulStartTime;
            syncJob.Period = syncJobPatch.Period;
            syncJob.Query = syncJobPatch.Query;
            syncJob.StartDate = syncJobPatch.StartDate;
            syncJob.IgnoreThresholdOnce = syncJobPatch.IgnoreThresholdOnce;
            syncJob.ThresholdPercentageForAdditions = syncJobPatch.ThresholdPercentageForAdditions;
            syncJob.ThresholdPercentageForRemovals = syncJobPatch.ThresholdPercentageForRemovals;
            syncJob.IsDryRunEnabled = syncJobPatch.IsDryRunEnabled;
            syncJob.DryRunTimeStamp = syncJobPatch.DryRunTimeStamp;
            syncJob.ThresholdViolations = syncJobPatch.ThresholdViolations;

            return syncJob;
        }
    }
}
