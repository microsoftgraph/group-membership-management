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
            string clientId = AppSettings.LoadAppSettings().ClientId;
            string tenantId = AppSettings.LoadAppSettings().TenantId;

            // if this doesn't work, make sure localhost is set as a reply url under Native/Desktop Client!
            // it won't work if localhost is under web
            var publicClientApp = PublicClientApplicationBuilder.Create(clientId)
            .WithRedirectUri("http://localhost")
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();

			var authProvider = new InteractiveAuthenticationProvider(publicClientApp);
            var _graphServiceClient = new GraphServiceClient(authProvider);
            var users = new Users(_graphServiceClient);
            await users.addUsers();
        }
    }
}
