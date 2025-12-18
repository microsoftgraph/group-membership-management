// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using DIConcreteTypes;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mail;
using System;
using System.Runtime.CompilerServices;

namespace Common.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGraphAPIClient(this IServiceCollection services)
        {
            services.AddSingleton((services) =>
            {
                var tokenCredential = CreateGraphServiceClient(services);
                return new GraphServiceClient(tokenCredential);
            });

            return services;
        }

        public static IServiceCollection AddScopedMailRepository(this IServiceCollection services)
        {
            services.AddOptions<EmailSenderRecipient>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.SenderAddress = configuration.GetValue<string>("senderAddress");
                settings.SenderPassword = configuration.GetValue<string>("senderPassword");
                settings.SupportEmailAddresses = configuration.GetValue<string>("supportEmailAddresses");
            });

            services.AddSingleton<IEmailSenderRecipient>(services =>
            {
                var creds = services.GetService<IOptions<EmailSenderRecipient>>();
                return new EmailSenderRecipient(
                    creds.Value.SenderAddress,
                    creds.Value.SenderPassword,
                    creds.Value.SupportEmailAddresses);
            });

            services.AddSingleton<IMailConfig>(services =>
            {
                var configuration = services.GetService<IConfiguration>();
                return new MailConfig(configuration.GetValue<bool>("Mail:IsAdaptiveCardEnabled"),
                    configuration.GetValue("Mail:IsMailApplicationPermissionGranted", false),
                    configuration.GetValue<string>("senderAddress"),
                    configuration.GetValue("Mail:SkipMailNotifications", false));
            });

            services.AddScoped<IMailRepository>(provider =>
            {
                var mailConfig = provider.GetService<IMailConfig>();
                var graphCredentials = provider.GetService<IOptions<GraphCredentials>>().Value;

                TokenCredential graphTokenCredential;

                if (mailConfig.GMMHasSendMailApplicationPermissions)
                {
                    graphTokenCredential = CreateGraphServiceClient(provider);
                }
                else
                {
                    var mailCredentials = provider.GetService<IOptions<EmailSenderRecipient>>();
                    graphCredentials.ServiceAccountUserName = mailCredentials.Value.SenderAddress;
                    graphCredentials.ServiceAccountPassword = mailCredentials.Value.SenderPassword;
                    graphTokenCredential = FunctionAppDI.CreateServiceAccountAuthProvider(graphCredentials);
                }

                return new MailRepository(
                    new GraphServiceClient(graphTokenCredential),
                        provider.GetService<IMailConfig>(),
                        provider.GetService<ILocalizationRepository>(),
                        provider.GetService<ILoggingRepository>(),
                        GetValueOrDefault("actionableEmailProviderId"),
                        provider.GetService<IGraphGroupRepository>(),
                        provider.GetService<IDatabaseSettingsRepository>(),
                        provider.GetService<IRetryPolicyProvider>(),
                        provider.GetRequiredService<TelemetryClient>()
                        );
            });

            return services;
        }

        public static AuthenticationType MapStringToAuthenticationType(string input)
        {
            if (Enum.TryParse(typeof(AuthenticationType), input, true, out object result))
            {
                return (AuthenticationType)result;
            }
            else
            {
                return AuthenticationType.Unknown;
            }
        }

        private static TokenCredential CreateGraphServiceClient(IServiceProvider provider)
        {
            var configuration = provider.GetService<IConfiguration>();
            var graphCredentials = provider.GetService<IOptions<GraphCredentials>>().Value;

            if (graphCredentials.AuthenticationType == AuthenticationType.Unknown)
            {
                graphCredentials.AuthenticationType = MapStringToAuthenticationType(configuration["GraphAPI:AuthenticationType"]);
            }

            return FunctionAppDI.CreateAuthenticationProvider(graphCredentials, graphCredentials.AuthenticationType);
        }

        private static string GetValueOrDefault(string key, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            return Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process) ?? string.Empty;
        }
    }
}

