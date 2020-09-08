// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string clientId = AppSettings.LoadAppSettings().ClientId;
            string tenantId = AppSettings.LoadAppSettings().TenantId;

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
