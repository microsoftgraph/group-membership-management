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

namespace WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

            var azureADConfigSection = builder.Configuration.GetSection("AzureAd");
            var tenantId = azureADConfigSection.GetValue<string>("TenantId");
            var clientId = azureADConfigSection.GetValue<string>("ClientId");
            var instanceUrl = azureADConfigSection.GetValue<string>("Instance");
            instanceUrl += instanceUrl.Last() == '/' ? string.Empty : "/";

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

            builder.Services
               .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
               .AddMicrosoftIdentityWebApi(azureADConfigSection);

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
                            AuthorizationUrl = new Uri($"{instanceUrl}{tenantId}/oauth2/authorize"),
                            TokenUrl = new Uri($"{instanceUrl}{tenantId}/oauth2/token")
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

            builder.Services.AddOptions<ThresholdNotificationServiceConfig>().Configure<IConfiguration>((settings, configuration) =>
            {
                settings.ActionableEmailProviderId = configuration.GetValue<Guid>("Settings:ActionableEmailProviderId");
                settings.ApiHostname = configuration.GetValue<string>("Settings:ApiHostname");
            });
            builder.Services.AddScoped<IThresholdNotificationService, ThresholdNotificationService>();

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
                        options.OAuthClientId(clientId);
                        options.OAuthUseBasicAuthenticationWithAccessCodeGrant();
                        options.OAuthAdditionalQueryStringParams(new Dictionary<string, string> {
                                { "scope", $"https://{clientId}/.default" },
                                { "nonce", Guid.NewGuid().ToString() },
                                { "resource", clientId }
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
    }
}