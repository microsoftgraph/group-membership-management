using Microsoft.AspNetCore.Authorization;

namespace WebAppWithAuth.Authentication
{
    public class GroupRequirement : IAuthorizationRequirement
    {
        public string GroupGuid { get; }
        public GroupRequirement()
        {

        }
        public GroupRequirement(string groupGuid) { GroupGuid = groupGuid; }
    }
}
