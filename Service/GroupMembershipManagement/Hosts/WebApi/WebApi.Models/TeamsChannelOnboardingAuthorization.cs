// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Security.Claims;

namespace WebApi.Models
{
    /// <summary>
    /// US6: Effective authorization rule for TeamsChannel onboarding/creation entry points.
    /// Allow when the caller is a JOB_TENANT_WRITER (a superset that already includes this permission)
    /// OR is a JOB_OWNER_WRITER that additionally holds TEAMS_CHANNEL_ONBOARDER. This AND/superset logic
    /// cannot be expressed with a single [Authorize(Roles=...)] string, so it is centralized here and used
    /// by the controller gates and by the defense-in-depth re-checks in the handlers.
    /// </summary>
    public static class TeamsChannelOnboardingAuthorization
    {
        public static bool IsAuthorized(ClaimsPrincipal user)
        {
            if (user == null)
                return false;

            if (user.IsInRole(Roles.JOB_TENANT_WRITER))
                return true;

            return user.IsInRole(Roles.JOB_OWNER_WRITER) && user.IsInRole(Roles.TEAMS_CHANNEL_ONBOARDER);
        }
    }
}
