// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using WebApi.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Microsoft.OpenApi.Models;
using Repositories.Contracts;
using Repositories.Logging;
using Microsoft.Extensions.Options;
using DIConcreteTypes;
using Repositories.SyncJobsRepository;
using Microsoft.AspNetCore.OData;
using Common.DependencyInjection;
using Microsoft.Graph;
using Repositories.GraphGroups;
using Services.Contracts.Notifications;
using Services.Notifications;
using Repositories.NotificationsRepository;
using Repositories.Localization;
using Microsoft.O365.ActionableMessages.Utilities;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Repositories.Contracts.InjectConfig;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Repositories.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Repositories.EntityFramework;

namespace WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

            var azureAdConfigSection = builder.Configuration.GetSection("AzureAd");
            var azureAdTenantId = azureAdConfigSection.GetValue<string>("TenantId");
            var azureAdClientId = azureAdConfigSection.GetValue<string>("ClientId");
            var azureAdInstanceUrl = azureAdConfigSection.GetValue<string>("Instance");
            azureAdInstanceUrl += azureAdInstanceUrl.Last() == '/' ? string.Empty : "/";
            var azureAdAudience = azureAdConfigSection.GetValue<string>("Audience");

            var apiHostName = builder.Configuration.GetValue<string>("Settings:ApiHostname");
            var secureApiHostName = $"https://{apiHostName}";

            builder.Services.AddDbContext<GMMContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("JobsContext")));

            builder.Services.Configure<WebAPISettings>(builder.Configuration.GetSection("WebAPI:Settings"));
            builder.Configuration.AddAzureAppConfiguration(options =>
            {
                var appConfigurationEndpoint = builder.Configuration.GetValue<string>("Settings:appConfigurationEndpoint");
                options.Connect(new Uri(appConfigurationEndpoint), new DefaultAzureCredential())
                    .Select("WebAPI:*")
                    .ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("WebAPI:Settings:Sentinel", refreshAll: true);
                    });
            });

            // Add services to the container.
            builder.Services.AddAzureAppConfiguration();

            builder.Services.AddSingleton(sp =>
            {
                var telemetryConfiguration = new TelemetryConfiguration();
                telemetryConfiguration.InstrumentationKey = builder.Configuration.GetValue<string>("Settings:APPINSIGHTS_INSTRUMENTATIONKEY");
                telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                var tc = new TelemetryClient(telemetryConfiguration);
                tc.Context.Operation.Name = "WebAPI";
                return tc;
            });

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(azureAdConfigSection);

            builder.Services.AddSingleton<ActionableMessageTokenValidator>();

            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, async options =>
            {
                var tenantSigningKeys = await getSigningKeysFromUrlAsync($"{azureAdInstanceUrl}{azureAdTenantId}/.well-known/openid-configuration");
                var tenantSigningKeysv2 = await getSigningKeysFromUrlAsync($"{azureAdInstanceUrl}{azureAdTenantId}/v2.0/.well-known/openid-configuration");
                var officeSigningKeys = await getSigningKeysFromUrlAsync("https://substrate.office.com/sts/common/.well-known/openid-configuration");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudiences = new[] {
                        azureAdAudience,
                        secureApiHostName
                    },
                    ValidateIssuer = true,
                    ValidIssuers = new[] {
                        $"https://sts.windows.net/{azureAdTenantId}/",
                        "https://substrate.office.com/sts/"
                    },
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = tenantSigningKeys.Concat(tenantSigningKeysv2).Concat(officeSigningKeys)
                };
            });

            builder.Services.AddApiVersioning(opt =>
                {
                    opt.DefaultApiVersion = new ApiVersion(1, 0);
                    opt.AssumeDefaultVersionWhenUnspecified = true;
                    opt.ReportApiVersions = true;
                    opt.ApiVersionReader = new UrlSegmentApiVersionReader();
                });

            // Add ApiExplorer to discover versions
            builder.Services.AddVersionedApiExplorer(setup =>
            {
                setup.GroupNameFormat = "'v'VVV";
                setup.SubstituteApiVersionInUrl = true;
            });

            builder.Services.AddControllers()
                            .AddOData(options => options.Select().Filter());
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                // Enabled OAuth security in Swagger
                options.AddSecurityDefinition("WebApiAuth2", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        Implicit = new OpenApiOAuthFlow()
                        {
                            AuthorizationUrl = new Uri($"{azureAdInstanceUrl}{azureAdTenantId}/oauth2/authorize"),
                            TokenUrl = new Uri($"{azureAdInstanceUrl}{azureAdTenantId}/oauth2/token")
                        }
                    }
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "WebApiAuth2" }
                            },
                            new[] { "user_impersonation" }
                        }
                    });

            });

            builder.Services.AddCors();

            builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();
            builder.Services.AddApplicationInsightsTelemetry();

            builder.Services.InjectMessageHandlers();

            builder.Services.AddLocalization(options =>
            {
                options.ResourcesPath = "Resources";
            });
            builder.Services.AddSingleton<ILocalizationRepository, LocalizationRepository>();

            builder.Services.AddOptions<LogAnalyticsSecret<LoggingRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.WorkSpaceId = configuration.GetValue<string>("Settings:logAnalyticsCustomerId");
                settings.SharedKey = configuration.GetValue<string>("Settings:logAnalyticsPrimarySharedKey");
                settings.Location = "WebAPI";
            })
            .Services.AddSingleton<ILoggingRepository, LoggingRepository>(services =>
            {
                var settings = services.GetRequiredService<IOptions<LogAnalyticsSecret<LoggingRepository>>>();
                return new LoggingRepository(settings.Value);
            });

            builder.Services.AddOptions<SyncJobRepoCredentials<SyncJobRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("Settings:jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("Settings:jobsTableName");
            })
            .Services.AddSingleton<ISyncJobRepository>(services =>
            {
                var settings = services.GetRequiredService<IOptions<SyncJobRepoCredentials<SyncJobRepository>>>();
                return new SyncJobRepository(settings.Value.ConnectionString, settings.Value.TableName, services.GetService<ILoggingRepository>());
            });

            builder.Services.AddOptions<NotificationRepoCredentials<NotificationRepository>>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ConnectionString = configuration.GetValue<string>("Settings:jobsStorageAccountConnectionString");
                settings.TableName = configuration.GetValue<string>("Settings:notificationsTableName");
            });
            builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();

            builder.Services.Configure<GraphCredentials>(builder.Configuration.GetSection("Settings:GraphCredentials"))
            .AddSingleton((services) =>
            {
                return new GraphServiceClient(FunctionAppDI.CreateAuthenticationProvider(services.GetRequiredService<IOptions<GraphCredentials>>().Value));
            })
            .AddScoped<IGraphGroupRepository, GraphGroupRepository>();


            builder.Services.AddOptions<HandleInactiveJobsConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.HandleInactiveJobsEnabled = GetBoolSetting(configuration, "AzureMaintenance:HandleInactiveJobsEnabled", false);
                settings.NumberOfDaysBeforeDeletion = GetIntSetting(configuration, "AzureMaintenance:NumberOfDaysBeforeDeletion", 0);
            });
            builder.Services.AddSingleton<IHandleInactiveJobsConfig>(services =>
            {
                return new HandleInactiveJobsConfig(
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.HandleInactiveJobsEnabled,
                    services.GetService<IOptions<HandleInactiveJobsConfig>>().Value.NumberOfDaysBeforeDeletion);
            });

            builder.Services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ActionableEmailProviderId = configuration.GetValue<Guid>("Settings:ActionableEmailProviderId");
                settings.ApiHostname = apiHostName;
            });
            builder.Services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();
            builder.Services.AddScoped<IDatabaseMigrationsRepository, DatabaseMigrationsRepository>();
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                IdentityModelEventSource.ShowPII = true;

                app.UseDeveloperExceptionPage();

                var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
                    {
                        var url = $"/swagger/{description.GroupName}/swagger.json";
                        options.SwaggerEndpoint(url, description.GroupName.ToUpperInvariant());
                        options.OAuthAppName("Swagger Client");
                        options.OAuthClientId(azureAdClientId);
                        options.OAuthUseBasicAuthenticationWithAccessCodeGrant();
                        options.OAuthAdditionalQueryStringParams(new Dictionary<string, string> {
                                { "scope", $"https://{azureAdClientId}/.default" },
                                { "nonce", Guid.NewGuid().ToString() },
                                { "resource", azureAdClientId }
                            });
                    }
                });

            }

            app.UseAzureAppConfiguration();

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            var allowedOrigins = new[] { "https://*.microsoft.com", "http://localhost:3000" };
            app.UseCors(x => x
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .Build()
            );

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }

        private static async Task<ICollection<SecurityKey>> getSigningKeysFromUrlAsync(string url)
        {
            var openIdConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                url,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            var config = await openIdConfigManager.GetConfigurationAsync();

            return config.SigningKeys;
        }

        private static bool GetBoolSetting(IConfiguration configuration, string settingName, bool defaultValue)
        {
            var checkParse = bool.TryParse(configuration[settingName], out bool value);
            if (checkParse)
                return value;
            return defaultValue;
        }

        private static int GetIntSetting(IConfiguration configuration, string settingName, int defaultValue)
        {
            var checkParse = int.TryParse(configuration[settingName], out int value);
            if (checkParse)
                return value;
            return defaultValue;
        }
    }
}