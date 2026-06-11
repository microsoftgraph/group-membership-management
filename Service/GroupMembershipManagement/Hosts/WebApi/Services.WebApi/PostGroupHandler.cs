// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace Services
{
    public class PostGroupHandler : RequestHandlerBase<PostGroupRequest, PostGroupResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;

        public PostGroupHandler(ILoggingRepository loggingRepository, IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<PostGroupResponse> ExecuteCoreAsync(PostGroupRequest request)
        {
            var response = new PostGroupResponse();
            try
            {
                var group = await _graphGroupRepository.CreateGroupFromUI(request.GroupName, request.UserIdentity, request.GroupAlias);

                if (group == null)
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    response.ErrorCode = "GroupCreationFailed";
                    response.ResponseData = new List<string> { "Group creation failed." };
                }
                else
                {
                    response.StatusCode = HttpStatusCode.OK;
                    response.GroupId = group.ObjectId;
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorCode = "GroupCreationException";
                response.ResponseData = new List<string> { ex.Message };
            }

            return response;
        }
    }
}