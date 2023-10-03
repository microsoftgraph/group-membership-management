// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;

namespace WebApi.Configuration
{
    public static class MessageHandlerInjector
    {
        public static IServiceCollection InjectMessageHandlers(this IServiceCollection services)
        {
            services.AddTransient<IRequestHandler<SearchGroupsRequest, SearchGroupsResponse>, SearchGroupsHandler>();

            services.AddTransient<IRequestHandler<GetJobsRequest, GetJobsResponse>, GetJobsHandler>();
            services.AddTransient<IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse>, GetJobDetailsHandler>();

            services.AddTransient<IRequestHandler<NotificationCardRequest, NotificationCardResponse>, NotificationCardHandler>();

            services.AddTransient<IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse>, ResolveNotificationHandler>();
            return services;
        }
    }
}
