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
            services.AddTransient<IRequestHandler<SearchDestinationsRequest, SearchDestinationsResponse>, SearchDestinationsHandler>();
            services.AddTransient<IRequestHandler<GetGroupEndpointsRequest, GetGroupEndpointsResponse>, GetGroupEndpointsHandler>();
            services.AddTransient<IRequestHandler<GetGroupOnboardingStatusRequest, GetGroupOnboardingStatusResponse>, GetGroupOnboardingStatusHandler>();
            
            services.AddTransient<IRequestHandler<GetSettingRequest, GetSettingResponse>, GetSettingHandler>();
            services.AddTransient<IRequestHandler<GetAllSettingsRequest, GetAllSettingsResponse>, GetAllSettingsHandler>();
            services.AddTransient<IRequestHandler<PatchSettingRequest, NullResponse>, PatchSettingHandler>();

            services.AddTransient<IRequestHandler<GetDefaultSqlMembershipSourceRequest, GetDefaultSqlMembershipSourceResponse>, GetDefaultSqlMembershipSourceHandler>();
            services.AddTransient<IRequestHandler<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse>, GetDefaultSqlMembershipSourceAttributesHandler>();
            services.AddTransient<IRequestHandler<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse>, PatchDefaultSqlMembershipSourceCustomLabelHandler>();
            services.AddTransient<IRequestHandler<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse>, PatchDefaultSqlMembershipSourceAttributesHandler>();

            services.AddTransient<IRequestHandler<GetJobsRequest, GetJobsResponse>, GetJobsHandler>();
            services.AddTransient<IRequestHandler<GetJobDetailsRequest, GetJobDetailsResponse>, GetJobDetailsHandler>();

            services.AddTransient<IRequestHandler<NotificationCardRequest, NotificationCardResponse>, NotificationCardHandler>();

            services.AddTransient<IRequestHandler<ResolveNotificationRequest, ResolveNotificationResponse>, ResolveNotificationHandler>();

            services.AddTransient<IRequestHandler<GetOrgLeaderDetailsRequest, GetOrgLeaderDetailsResponse>, GetOrgLeaderDetailsHandler>();

            services.AddTransient<IRequestHandler<PatchJobRequest, PatchJobResponse>, PatchJobHandler>();
            services.AddTransient<IRequestHandler<PostJobRequest, PostJobResponse>, PostJobHandler>();

            return services;
        }
    }
}
