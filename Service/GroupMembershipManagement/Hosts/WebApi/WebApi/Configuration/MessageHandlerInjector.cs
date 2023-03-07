// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace WebApi.Configuration
{
    public static class MessageHandlerInjector
    {
        public static IServiceCollection InjectMessageHandlers(this IServiceCollection services)
        {
            services.AddTransient<IRequestHandler<GetJobsRequest, GetJobsResponse>, GetJobsHandler>();

            return services;
        }
    }
}
