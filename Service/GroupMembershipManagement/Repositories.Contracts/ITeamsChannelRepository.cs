using Models.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ITeamsChannelRepository
    {
        public Task<IEnumerable<AzureADTeamsUser>> ReadUsersFromChannel(Guid groupId, string channelId);
    }
}
