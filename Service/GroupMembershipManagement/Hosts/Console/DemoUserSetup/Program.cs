// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace DemoUserSetup
{
    class Program
    {
        static async Task Main(string[] args)
        {
			// use an Azure AD app in your demo tenant with http://localhost as a redirect URI under "Mobile and Desktop Applications"
			// (it won't work if it's under Web, which is the default!)
			// and the following delegated permissions:
			// - GroupMember.Read.All
			// - User.Read.All
			// When you run this, sign in with your demo tenant administrator credentials.
            string clientId = AppSettings.LoadAppSettings().ClientId;
            string tenantId = AppSettings.LoadAppSettings().TenantId;

			var publicClientApp = PublicClientApplicationBuilder.Create(clientId)
            .WithRedirectUri("http://localhost")
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();

			var authProvider = new InteractiveAuthenticationProvider(publicClientApp);
            var _graphServiceClient = new GraphServiceClient(authProvider);
            var users = new Users(_graphServiceClient);
            await users.AddUsers();
        }
    }
}
