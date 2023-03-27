using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Polly;
using Polly.Extensions.Http;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.TeamsChannel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TeamsChannel.Service;
using TeamsChannel.Service.Contracts;
using Constants = TeamsChannel.Service.Constants;

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

            builder.Services.AddHttpClient(Constants.MembershipAggregatorHttpClientName, (services, httpClient) =>
            {
                var configuration = services.GetService<IConfiguration>();
                httpClient.BaseAddress = new Uri(configuration["membershipAggregatorUrl"]);
                httpClient.DefaultRequestHeaders.Add("x-functions-key", configuration["membershipAggregatorFunctionKey"]);
            }).AddPolicyHandler(GetRetryPolicy());
            builder.Services.AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetService<IOptions<GraphCredentials>>().Value));
            })
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];

                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddTransient<ITeamsChannelService, TeamsChannelService>()
            .AddTransient<ITeamsChannelRepository, TeamsChannelRepository>();
        }

        // see https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-http-call-retries-exponential-backoff-polly
        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode != System.Net.HttpStatusCode.NoContent)
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }
    }
}
