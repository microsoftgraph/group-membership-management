// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Services.Contracts;
using SqlMembershipObtainer.Common.DependencyInjection;

namespace Services
{
    public class DataFactoryService : IDataFactoryService
    {
        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);
        private SqlMembershipADFCache _sqlMembershipADFCache = new SqlMembershipADFCache();

        private IDataFactoryRepository _dataFactoryRepository;
        private ILoggingRepository _loggingRepository;

        public DataFactoryService(IDataFactoryRepository dataFactoryRepository, ILoggingRepository loggingRepository)
        {
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<string> GetMostRecentSucceededRunIdAsync(Guid? runId)
        {
            await _adfRunIdSemaphore.WaitAsync();

            if (string.IsNullOrWhiteSpace(_sqlMembershipADFCache.LastSqlMembershipRunId) || (DateTime.UtcNow - _sqlMembershipADFCache.RunDateTime).TotalHours >= 1)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = "Getting most recent ADF run id.", RunId = runId });
                _sqlMembershipADFCache.LastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                _sqlMembershipADFCache.RunDateTime = DateTime.UtcNow;
            }

            _adfRunIdSemaphore.Release();

            if (string.IsNullOrWhiteSpace(_sqlMembershipADFCache.LastSqlMembershipRunId))
            {
                var message = $"No SqlMembershipObtainer pipeline run has been found";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = message, RunId = runId });
                throw new ArgumentException(message);
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
