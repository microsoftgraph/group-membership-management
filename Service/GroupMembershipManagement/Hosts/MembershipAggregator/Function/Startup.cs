// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.FunctionBase;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repositories.BlobStorage;
using Repositories.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.MembershipAggregator.Startup))]

namespace Hosts.MembershipAggregator
{
    public class Startup : CommonStartup
    {
        protected override string FunctionName => nameof(MembershipAggregator);
        protected override string DryRunSettingName => string.Empty;

        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);

            builder.Services.AddSingleton<IBlobStorageRepository, BlobStorageRepository>((s) =>
              {
                  var configuration = s.GetService<IConfiguration>();
                  var storageAccountName = configuration["membershipStorageAccountName"];
                  var containerName = configuration["membershipContainerName"];

                  return new BlobStorageRepository($"https://{storageAccountName}.blob.core.windows.net/{containerName}");
              });
        }
    }
}
