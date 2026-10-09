// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class PostGroupHandler : RequestHandlerBase<PostGroupRequest, PostGroupResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private const string GmmOwnerSource = "ServicePrincipalLookup";

        public PostGroupHandler(ILogger<PostGroupHandler> logger,
                                IGraphGroupRepository graphGroupRepository) : base(logger)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<PostGroupResponse> ExecuteCoreAsync(PostGroupRequest request)
        {
            var response = new PostGroupResponse();
            try
            {
                var group = await _graphGroupRepository.CreateGroupFromUI(request.GroupName, request.UserIdentity, request.GroupAlias, request.GroupType);

                if (group == null)
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    response.ErrorCode = "GroupCreationFailed";
                    response.ResponseData = new List<string> { "Group creation failed." };

                    Logger.LogError(
                        "Group creation attempt. Requester={RequesterObjectId} Name={GroupName} Type={GroupType} Outcome={Outcome} FailureReason={FailureReason} OwnerSource={GmmOwnerSource}",
                        request.UserIdentity, request.GroupName, request.GroupType, "Failed", response.ErrorCode, GmmOwnerSource);
                }
                else
                {
                    response.StatusCode = HttpStatusCode.OK;
                    response.GroupId = group.ObjectId;

                    Logger.LogInformation(
                        "Group creation attempt. Requester={RequesterObjectId} Group={GroupObjectId} Name={GroupName} Type={GroupType} Outcome={Outcome} OwnerSource={GmmOwnerSource}",
                        request.UserIdentity, group.ObjectId, request.GroupName, request.GroupType, "Succeeded", GmmOwnerSource);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorCode = "GroupCreationException";
                response.ResponseData = new List<string> { "Group creation failed." };

                Logger.LogError(ex,
                    "Group creation attempt. Requester={RequesterObjectId} Name={GroupName} Type={GroupType} Outcome={Outcome} FailureReason={FailureReason} OwnerSource={GmmOwnerSource}",
                    request.UserIdentity, request.GroupName, request.GroupType, "Failed", response.ErrorCode, GmmOwnerSource);
            }

            return response;
        }
    }
}