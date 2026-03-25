// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlMembershipObtainer;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Services.Contracts;

namespace Services
{
    public class DataFactoryService : IDataFactoryService
    {
        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);
        private SqlMembershipADFCache _sqlMembershipADFCache = new SqlMembershipADFCache();

        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ILogger<DataFactoryService> _logger;

        public DataFactoryService(IDataFactoryRepository dataFactoryRepository, ILogger<DataFactoryService> logger)
        {
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GetMostRecentSucceededRunIdAsync(Guid? runId)
        {
            await _adfRunIdSemaphore.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(_sqlMembershipADFCache.LastSqlMembershipRunId) || (DateTime.UtcNow - _sqlMembershipADFCache.RunDateTime).TotalHours >= 1)
                {
                    _logger.GettingAdfRunId();
                    _sqlMembershipADFCache.LastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                    _sqlMembershipADFCache.RunDateTime = DateTime.UtcNow;
                }
            }
            finally
            {
                _adfRunIdSemaphore.Release();
            }

            if (string.IsNullOrWhiteSpace(_sqlMembershipADFCache.LastSqlMembershipRunId))
            {
                _logger.NoPipelineRunFound();
                throw new ArgumentException("No SqlMembershipObtainer pipeline run has been found");
            }

            return _sqlMembershipADFCache.LastSqlMembershipRunId;
        }
    }

    internal class SqlMembershipADFCache
    {
        public string? LastSqlMembershipRunId { get; set; }
        public DateTime RunDateTime { get; set; }
    }
}