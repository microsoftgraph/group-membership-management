using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    public class DefaultRuntimeRetrievalService : IRuntimeRetrievalService
    {
        private readonly double DEFAULT_RUNTIME_SECONDS;

        public DefaultRuntimeRetrievalService(double defaultRuntimeSeconds)
        {
            DEFAULT_RUNTIME_SECONDS = defaultRuntimeSeconds;
        }

        public Task<Dictionary<Guid, double>> GetRuntimes(List<Guid> groupIds)
        {
            Dictionary<Guid, double> runtimes = new Dictionary<Guid, double>();
            foreach(Guid groupId in groupIds)
            {
                if(!runtimes.ContainsKey(groupId))
                    runtimes.Add(groupId, DEFAULT_RUNTIME_SECONDS);
            }

            return Task.FromResult(runtimes);
        }
    }
}
