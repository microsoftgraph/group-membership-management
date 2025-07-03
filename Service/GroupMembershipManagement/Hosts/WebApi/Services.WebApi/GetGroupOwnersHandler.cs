// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using GroupOwnerDTO = WebApi.Models.DTOs.GroupOwner;

namespace Services
{
    public class GetGroupOwnersHandler : RequestHandlerBase<GetGroupOwnersRequest, GetGroupOwnersResponse>
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILoggingRepository _loggingRepository;

        public GetGroupOwnersHandler(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        protected override async Task<GetGroupOwnersResponse> ExecuteCoreAsync(GetGroupOwnersRequest request)
        {
            var response = new GetGroupOwnersResponse();

            try
            {
                var owners = await _graphGroupRepository.GetGroupOwnersAsync(request.GroupId, 0);
                response.Owners = owners.Select(owner => new GroupOwnerDTO(
                    owner.ObjectId,
                    owner.DisplayName ?? "",
                    owner.Mail ?? "")).ToList();
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Unable to retrieve group owners for group {request.GroupId}\n{ex.Message}"
                });
                throw;
            }

            return response;
        }
    }
}
