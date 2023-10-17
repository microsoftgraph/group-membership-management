// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Services.WebApi.Contracts;
using WebApi.Models.DTOs;

namespace Services.WebApi.Validators
{
    public class StatusValidator : IValidator<SyncJobPatch>
    {
        public ValidationResponse Validate(SyncJobPatch syncJobPatch)
        {
            if (string.IsNullOrWhiteSpace(syncJobPatch.Status))
                return new ValidationResponse { IsValid = false, ErrorCode = "StatusIsRequired" };

            if (!Enum.TryParse<SyncStatus>(syncJobPatch.Status, true, out var _))
                return new ValidationResponse { IsValid = false, ErrorCode = $"StatusIsNotValid" };

            return new ValidationResponse { IsValid = true };
        }
    }
}
