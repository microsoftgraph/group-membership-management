// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class NonProdService : INonProdService
    {
        private readonly ILoggingRepository _loggingRepository;

        public NonProdService(
            ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public MembershipDifference GetMembershipDifference(List<AzureADUser> currentMembership, List<AzureADUser> targetMembership)
        {
            if(currentMembership == null || targetMembership == null)
            {
                throw new ArgumentNullException("Either the current or target membership input was null. GetMembershipDifference inputs must be lists");
            }

            var usersToAdd = targetMembership.Where(x => !currentMembership.Contains(x)).ToList();
            var usersToRemove = currentMembership.Where(x => !targetMembership.Contains(x)).ToList();

            return new MembershipDifference
            {
                UsersToAdd = usersToAdd,
                UsersToRemove = usersToRemove
            };
        }
    }
}
