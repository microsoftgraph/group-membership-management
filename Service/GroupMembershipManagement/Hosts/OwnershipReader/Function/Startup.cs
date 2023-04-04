// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.BlobStorage;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.OwnershipReader.Startup))]
namespace Hosts.OwnershipReader
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(OwnershipReader);

        protected override string DryRunSettingName => "OwnershipReader:IsDryRunEnabled";

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IGraphServiceClient>((services) =>
            {
                var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(graphCredentials));
            })
            .AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
            {
                var configuration = s.GetService<IConfiguration>();
                var storageAccountName = configuration["membershipStorageAccountName"];
                var containerName = configuration["membershipContainerName"];
                return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>()
            .AddScoped<IOwnershipReaderService, OwnershipReaderService>();
        }
    }
}
