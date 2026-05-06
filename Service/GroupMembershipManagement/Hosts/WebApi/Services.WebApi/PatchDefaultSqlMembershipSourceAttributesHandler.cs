// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class PatchDefaultSqlMembershipSourceAttributesHandler : RequestHandlerBase<PatchDefaultSqlMembershipSourceAttributesRequest, NullResponse>
    {
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        public PatchDefaultSqlMembershipSourceAttributesHandler(ILogger<PatchDefaultSqlMembershipSourceAttributesHandler> logger,
                                IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository) : base(logger)
        {
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(PatchDefaultSqlMembershipSourceAttributesRequest request)
        {
            await _databaseSqlMembershipSourcesRepository.UpdateDefaultSourceAttributesAsync(request.Attributes);
            return new NullResponse();
        }
    }
}