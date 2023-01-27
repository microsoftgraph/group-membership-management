using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.TeamsChannel.Startup))]
namespace Hosts.TeamsChannel
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(TeamsChannel);

        protected override string DryRunSettingName => "TeamsChannel:IsTeamsChannelDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            //builder.Services.AddSingleton<GraphServiceClient>((services) =>
            //{
            //    return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            //})
            //.AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            //.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            //{
            //    var configuration = s.GetService<IConfiguration>();
            //    var storageAccountName = configuration["membershipStorageAccountName"];
            //    var containerName = configuration["membershipContainerName"];

            //    return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            //});
        }
    }
}
