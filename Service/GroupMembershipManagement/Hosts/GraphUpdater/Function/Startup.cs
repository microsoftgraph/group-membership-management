// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using DIConcreteTypes;
using Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using Repositories.Localization;
using Repositories.Logging;
using Repositories.Mail;
using Repositories.MembershipDifference;
using Repositories.SyncJobsRepository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// see https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection
[assembly: FunctionsStartup(typeof(Hosts.GraphUpdater.Startup))]

namespace Hosts.GraphUpdater
{
	public class Startup : FunctionsStartup
	{
		public override void Configure(IFunctionsHostBuilder builder)
		{
			builder.Services.AddOptions<GraphCredentials>().Configure<IConfiguration>((settings, configuration) =>
			{
				configuration.GetSection("graphCredentials").Bind(settings);
			});

			builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.ConnectionString = configuration.GetValue<string>("jobsStorageAccountConnectionString");
				settings.TableName = configuration.GetValue<string>("jobsTableName");
			});

			builder.Services.AddOptions<LogAnalyticsSecret<LoggingRepository>>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.WorkSpaceId = configuration.GetValue<string>("logAnalyticsCustomerId");
				settings.SharedKey = configuration.GetValue<string>("logAnalyticsPrimarySharedKey");
				settings.Location = nameof(GraphUpdater);
			});

			builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>()
			.AddSingleton<IGraphServiceClient>((services) =>
			{
				return new GraphServiceClient(FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value));
			})
			.AddScoped<IGraphGroupRepository, GraphGroupRepository>()
			.AddSingleton<ISyncJobRepository>(services =>
			{
				var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
				return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
			})
			.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(services => services.GetService<IOptions<LogAnalyticsSecret<LoggingRepository>>>().Value)
			.AddScoped<SessionMessageCollector>()
			.AddScoped<ILoggingRepository, LoggingRepository>()
			.AddScoped<IGraphUpdater, GraphUpdaterApplication>();

			var graphCredentials = builder.Services.BuildServiceProvider().GetService<IOptions<GraphCredentials>>().Value;
			builder.Services.AddOptions<EmailSender>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.Email = configuration.GetValue<string>("senderAddress");
				settings.Password = configuration.GetValue<string>("senderPassword");
			});
			builder.Services.AddSingleton<IMailRepository>(services => new MailRepository(new GraphServiceClient(FunctionAppDI.CreateMailAuthProvider(graphCredentials)), services.GetService<IOptions<EmailSender>>().Value, services.GetService<ILocalizationRepository>()));
			builder.Services.AddLocalization(opts => { opts.ResourcesPath = "Resources"; });
			builder.Services.Configure<RequestLocalizationOptions>(opts =>
			{
				var supportedCultures = new List<CultureInfo>
						{
							new CultureInfo("en-US"),
							new CultureInfo("es-ES"),
							new CultureInfo("hi-IN")
						};
				opts.DefaultRequestCulture = new RequestCulture("en-US");
				opts.SupportedCultures = supportedCultures;
				opts.SupportedUICultures = supportedCultures;
			});
			builder.Services.AddSingleton<ILocalizationRepository, LocalizationRepository>();
		}
    }

}
