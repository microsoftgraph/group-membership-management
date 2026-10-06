// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Notifications;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class ManagerOrgReaderFunction
    {
        private readonly ILogger<ManagerOrgReaderFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;
        private readonly INotificationSenderService _notificationSenderService;

        public ManagerOrgReaderFunction(ILogger<ManagerOrgReaderFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService, INotificationSenderService notificationSenderService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _notificationSenderService = notificationSenderService ?? throw new ArgumentNullException(nameof(notificationSenderService));
        }

        [Function(nameof(ManagerOrgReaderFunction))]
        public async Task<MembershipFileResult> ReadUsersAsync([ActivityTrigger] ManagerOrgReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(ManagerOrgReaderFunction));

                // A configured manager whose row is absent from the current HR data cannot anchor an org read. Surfacing
                // ManagerNotFound here makes the orchestrator disable the job before any membership is aggregated, so no
                // adds or removes are applied. A failed or transient read throws instead of returning false, so a stale
                // or unreadable table is never mistaken for an absent manager.
                var managerExists = await _sqlMembershipObtainerService.ManagerExistsAsync(
                    request.PersonnelNumber,
                    request.TableName,
                    request.SyncJob,
                    request.GroupId);

                if (!managerExists)
                {
                    _logger.LogInformation(
                        "Configured manager with personnel number {PersonnelNumber} was not found in table {TableName}; marking the job as {Status}.",
                        request.PersonnelNumber,
                        request.TableName,
                        SyncStatus.ManagerNotFound);

                    await _notificationSenderService.SendEmailAsync(
                        request.SyncJob,
                        NotificationMessageType.ManagerNotExistNotification,
                        new[]
                        {
                            request.GroupId.ToString(),
                            request.GroupId.ToString(),
                            request.PersonnelNumber.ToString(CultureInfo.InvariantCulture),
                            DisabledNotificationType.StatusDescriptions[NotificationMessageType.ManagerNotExistNotification],
                            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                        });

                    _logger.FunctionCompleted(nameof(ManagerOrgReaderFunction));

                    return new MembershipFileResult { Status = SyncStatus.ManagerNotFound };
                }

                var evaluationRequest =
                    SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                        request.PersonnelNumber,
                        request.Depth,
                        request.Filter,
                        request.IdentityTypes,
                        request.TableName);
                var evaluation =
                    await _sqlMembershipObtainerService.EvaluateMembershipAsync(
                        evaluationRequest,
                        request.SyncJob,
                        request.GroupId,
                        request.CurrentPart);
                var response =
                    await _sqlMembershipObtainerService.UploadMembershipEvaluationAsync(
                        evaluation,
                        request.SyncJob,
                        request.GroupId,
                        request.CurrentPart,
                        request.Exclusionary);

                _logger.FunctionCompleted(nameof(ManagerOrgReaderFunction));

                return response;
            }
        }
    }
}