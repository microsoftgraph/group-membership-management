// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class PatchDefaultSqlMembershipSourceCustomLabelHandler : RequestHandlerBase<PatchDefaultSqlMembershipSourceCustomLabelRequest, NullResponse>
    {
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        public PatchDefaultSqlMembershipSourceCustomLabelHandler(ILoggingRepository loggingRepository,
                                IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository) : base(loggingRepository)
        {
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
        }

        protected override async Task<NullResponse> ExecuteCoreAsync(PatchDefaultSqlMembershipSourceCustomLabelRequest request)
        {
            await _databaseSqlMembershipSourcesRepository.UpdateDefaultSourceCustomLabelAsync(request.CustomLabel);
            return new NullResponse();
        }
    }
}