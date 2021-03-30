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

			builder.Services.AddSingleton<IMembershipDifferenceCalculator<AzureADUser>, MembershipDifferenceCalculator<AzureADUser>>();

			builder.Services.AddSingleton<IGraphServiceClient>((services) =>
			{
				return new GraphServiceClient(FunctionAppDI.CreateAuthProvider(services.GetService<IOptions<GraphCredentials>>().Value));
			})
			.AddSingleton<IGraphGroupRepository, GraphGroupRepository>()
			.AddSingleton<ISyncJobRepository>(services =>
			{
				var creds = services.GetService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
				return new SyncJobRepository(creds.Value.ConnectionString, creds.Value.TableName, services.GetService<ILoggingRepository>());
			})
			.AddSingleton<IEmailSenderRecipient>(services =>
			{
				var creds = services.GetService<IOptions<EmailSenderRecipient>>();
				return new EmailSenderRecipient(creds.Value.SenderAddress, creds.Value.SenderPassword, creds.Value.SyncCompletedCCAddresses, creds.Value.SyncDisabledCCAddresses);
			})
			.AddSingleton<ILogAnalyticsSecret<LoggingRepository>>(services => services.GetService<IOptions<LogAnalyticsSecret<LoggingRepository>>>().Value)
			.AddSingleton<ILoggingRepository, LoggingRepository>()
			.AddSingleton<SessionMessageCollector>()
			.AddSingleton<IGraphUpdater, GraphUpdaterApplication>();

			var graphCredentials = builder.Services.BuildServiceProvider().GetService<IOptions<GraphCredentials>>().Value;
			builder.Services.AddOptions<EmailSenderRecipient>().Configure<IConfiguration>((settings, configuration) =>
			{
				settings.SenderAddress = configuration.GetValue<string>("senderAddress");
				settings.SenderPassword = configuration.GetValue<string>("senderPassword");
				settings.SyncCompletedCCAddresses = configuration.GetValue<string>("syncCompletedCCEmailAddresses");
				settings.SyncDisabledCCAddresses = configuration.GetValue<string>("syncDisabledCCEmailAddresses");
			});
			builder.Services.AddSingleton<IMailRepository>(services => new MailRepository(new GraphServiceClient(FunctionAppDI.CreateMailAuthProvider(graphCredentials)), services.GetService<ILocalizationRepository>()));
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