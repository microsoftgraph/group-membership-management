// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Services.WebApi.Contracts;
using WebApi.Models;

namespace Services.WebApi
{
    public class OperationsTaskQueue : IOperationsTaskQueue
    {
        private readonly Queue<OperationDetails> _operations;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(1);
        private readonly ILoggingRepository _loggingRepository;

        public OperationsTaskQueue(ILoggingRepository loggingRepository)
        {
            _operations = new Queue<OperationDetails>();
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task<OperationDetails?> DequeueAsync()
        {
            if (_operations.Count == 0)
            {
                _signal.Release();
                return null;
            }

            var operation = _operations.Dequeue();
            await _loggingRepository.LogMessageAsync(new Models.LogMessage { Message = $"Dequeued operation {operation.Operation}" });

            _signal.Release();

            return await Task.FromResult(operation);
        }

        public async Task QueueAsync(OperationDetails operation)
        {
            await _signal.WaitAsync();

            await _loggingRepository.LogMessageAsync(new Models.LogMessage { Message = $"Queuing operation {operation.Operation}" });
            if (!_operations.Any(o => o.Operation == operation.Operation))
            {
                _operations.Enqueue(operation);
            }
        }
    }
}
