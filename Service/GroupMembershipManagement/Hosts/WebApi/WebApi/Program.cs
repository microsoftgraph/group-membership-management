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

namespace WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                // Enabled OAuth security in Swagger
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                            },
                            new[] { "user_impersonation" }
                        }
                    });

                options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
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
            });

            builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();
            builder.Services.AddApplicationInsightsTelemetry();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                IdentityModelEventSource.ShowPII = true;

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

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}