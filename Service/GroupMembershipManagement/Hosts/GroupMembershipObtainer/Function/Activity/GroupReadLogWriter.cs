// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System;
using System.Collections.Generic;

namespace Hosts.GroupMembershipObtainer
{
    internal static class GroupReadLogWriter
    {
        public static void ReaderSucceeded(
            ILogger logger,
            Guid objectId,
            MemberReadCounts counts,
            IReadOnlyList<string> identityTypes,
            GroupMembershipReadKind readKind)
        {
            counts ??= new MemberReadCounts();
            TryLog(() =>
            {
                var selection = GroupIdentitySelection.Resolve(identityTypes);
                var selectionDescription = GroupIdentitySelection.Describe(identityTypes);
                if (readKind == GroupMembershipReadKind.Full &&
                    selection.ToTypeNames().Count == 1)
                {
                    logger.SelectedFullReaderCounts(
                        objectId,
                        counts.MemberCount,
                        selection.ToTypeNames()[0],
                        counts.UserCount.GetValueOrDefault(),
                        counts.AgentCount.GetValueOrDefault(),
                        counts.ServicePrincipalCount,
                        counts.DeviceCount,
                        counts.ContactCount,
                        counts.GroupCount,
                        counts.UnknownTypeCount,
                        counts.OtherCount,
                        readKind.ToString(),
                        selectionDescription);
                    return;
                }

                if (readKind == GroupMembershipReadKind.Full)
                {
                    logger.FullReaderCounts(
                        objectId,
                        counts.MemberCount,
                        counts.UserCount.GetValueOrDefault(),
                        counts.AgentCount.GetValueOrDefault(),
                        counts.ServicePrincipalCount,
                        counts.DeviceCount,
                        counts.ContactCount,
                        counts.GroupCount,
                        counts.UnknownTypeCount,
                        counts.OtherCount,
                        readKind.ToString(),
                        selectionDescription);
                    return;
                }

                logger.DeltaReaderCounts(
                    objectId,
                    counts.MemberCount,
                    counts.ServicePrincipalCount,
                    counts.DeviceCount,
                    counts.ContactCount,
                    counts.GroupCount,
                    counts.UnknownTypeCount,
                    counts.OtherCount,
                    readKind.ToString(),
                    selectionDescription);
            });
        }

        public static void ReadCompleted(
            ILogger logger,
            int memberCount,
            Guid objectId,
            Guid targetGroupId,
            GroupMembershipReadKind readKind,
            IReadOnlyList<string> identityTypes) =>
            TryLog(() => logger.ReadMembersFromGroup(
                memberCount,
                objectId,
                targetGroupId,
                readKind.ToString(),
                GroupIdentitySelection.Describe(identityTypes)));

        public static void Fallback(
            ILogger logger,
            Guid objectId,
            string reason,
            GroupMembershipReadKind readKind,
            IReadOnlyList<string> identityTypes) =>
            TryLog(() => logger.FallingBackToFullRead(
                objectId,
                reason,
                readKind.ToString(),
                GroupIdentitySelection.Describe(identityTypes)));

        public static void SelectionStepFailed(
            ILogger logger,
            string stepName,
            Guid objectId,
            string errorMessage) =>
            TryLog(() => logger.SelectionStepFailed(stepName, objectId, errorMessage));

        public static void CleanupFailed(
            ILogger logger,
            Guid objectId,
            Exception exception) =>
            TryLog(() => logger.GroupPartCleanupFailed(objectId, exception.Message));

        public static void ReplacementReadCleanupIncomplete(
            ILogger logger,
            Guid objectId,
            string reason) =>
            TryLog(() => logger.ReplacementReadCleanupIncomplete(objectId, reason));

        private static void TryLog(Action log)
        {
            try
            {
                log();
            }
            catch
            {
                // Observability must never change membership processing.
            }
        }
    }
}
