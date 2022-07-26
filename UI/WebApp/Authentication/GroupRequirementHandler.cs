
using Microsoft.AspNetCore.Authorization;
using Microsoft.Graph;


namespace WebAppWithAuth.Authentication
{
    public class GroupRequirementHandler : AuthorizationHandler<GroupRequirement>
    {
        private GraphServiceClient graphServiceClient { get; set; }
        private string gmmAdminsGroupId { get; set; }
        public GroupRequirementHandler(GraphServiceClient client, string groupId)
        {
            graphServiceClient = client;
            gmmAdminsGroupId = groupId;
        }
        
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GroupRequirement requirement)
        {

            if (context.User.Identity == null || !(context.User.Identity.IsAuthenticated))
            {
                context.Fail();
            } 
            else
            {
                var queryOptions = new List<QueryOption>()
                {
                    new QueryOption("$count", "true")
                };

                var filter = $"id eq '{gmmAdminsGroupId}'";
                if (gmmAdminsGroupId == null)
                {
                    context.Fail();
                }
                
                var transitiveMemberOf = await graphServiceClient.Me.TransitiveMemberOf
                    .Request(queryOptions)
                    .Header("ConsistencyLevel", "eventual")
                    .Filter(filter)
                    .GetAsync();

                if (transitiveMemberOf.Count != 0)
                {
                    context.Succeed(requirement);
                }
            }
        }
    }
}