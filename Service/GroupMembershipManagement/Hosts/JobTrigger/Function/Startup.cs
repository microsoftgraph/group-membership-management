// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Hosts.FunctionCommon;
using Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Logging;
using Repositories.ServiceBusTopics;
using Repositories.SyncJobsRepository;
using Services.Contracts;

[assembly: FunctionsStartup(typeof(Hosts.JobTrigger.Startup))]

namespace Hosts.JobTrigger
{
    public class Startup : CommonStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            base.Configure(builder);
            builder.Services.AddSingleton<ISyncJobRepository>(new SyncJobRepository(GetValueOrThrow("jobsStorageAccountConnectionString"), GetValueOrThrow("jobsTableName")));
            builder.Services.AddSingleton<IServiceBusTopicsRepository>(new ServiceBusTopicsRepository(GetValueOrThrow("serviceBusConnectionString"), GetValueOrThrow("serviceBusSyncJobTopic")));
            builder.Services.AddSingleton<ISyncJobTopicService, SyncJobTopicsService>();
            builder.Services.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(new LogAnalyticsSecret<LoggingRepository>(GetValueOrThrow("logAnalyticsCustomerId"), GetValueOrThrow("logAnalyticsPrimarySharedKey"), nameof(JobTrigger)));
            builder.Services.AddSingleton<ILoggingRepository, LoggingRepository>();
        }
    }
}
