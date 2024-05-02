// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using System;

namespace Common.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGraphAPIClient(this IServiceCollection services)
        {
            services.AddSingleton((services) =>
            {
                var configuration = services.GetService<IConfiguration>();
                var graphCredentials = services.GetService<IOptions<GraphCredentials>>().Value;
                if (graphCredentials.AuthenticationType == AuthenticationType.Unknown)
                {
                    graphCredentials.AuthenticationType = MapStringToAuthenticationType(configuration["GraphAPI:AuthenticationType"]);
                }
                var credential = FunctionAppDI.CreateAuthenticationProvider(graphCredentials, graphCredentials.AuthenticationType);
                return new GraphServiceClient(credential);
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
    }
}

