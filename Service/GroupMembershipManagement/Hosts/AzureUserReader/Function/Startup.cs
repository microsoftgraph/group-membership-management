// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Common.DependencyInjection;
using DIConcreteTypes;
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphAzureADUsers;
using Services;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.AzureUserReader.Startup))]

namespace Hosts.AzureUserReader
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(AzureUserReader);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddGraphAPIClient();

            builder.Services.AddSingleton<IStorageAccountSecret>(services =>
                new StorageAccountSecret(GetValueOrThrow("storageAccountConnectionString")));

            builder.Services.AddScoped<IGraphUserRepository, GraphUserRepository>();
            builder.Services.AddScoped<IBlobClientFactory, BlobClientFactory>();
            builder.Services.AddScoped<IAzureUserReaderService, AzureUserReaderService>();
        }
    }
}