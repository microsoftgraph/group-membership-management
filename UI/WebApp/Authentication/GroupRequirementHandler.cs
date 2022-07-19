
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Authentication.WebAssembly.Msal;
using Microsoft.Graph;
using System.Net;
using System.Security.Claims;


namespace WebAppWithAuth.Authentication
{
    public class GroupRequirementHandler : AuthorizationHandler<GroupRequirement>
    {
        private GraphServiceClient graphServiceClient { get; set; }

        public GroupRequirementHandler(GraphServiceClient client)
        {
            graphServiceClient = client;
        }
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GroupRequirement requirement)
        {
            if (context.User.Identity == null || !(context.User.Identity.IsAuthenticated))
            {
                context.Fail();
            } 
            else
            {
                try
                {
                    var transitiveMemberOf = await graphServiceClient.Me.TransitiveMemberOf
                        .Request()
                        .Header("ConsistencyLevel", "eventual")
                        .Filter("id eq '6b4a9b13-6f63-4658-af10-9160a32f806c'")
                        .GetAsync();

                    if (transitiveMemberOf != null)
                    {
                        context.Succeed(requirement);
                    }
                } 
                catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
                {
                    // TODO: FIX THIS EXCEPTION HANDLING - TEST WITH DEMO TENANT ADMIN (MOD ADMIN)
                    context.Fail();
                }
            }

            // TODO: EDIT APP MANIFEST TO REMOVE CLAIMS STUFF

        }
    }
}