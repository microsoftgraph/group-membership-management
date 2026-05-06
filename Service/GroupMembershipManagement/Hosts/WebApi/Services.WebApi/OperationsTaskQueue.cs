// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Services.WebApi.Contracts;
using WebApi.Models;

namespace Services.WebApi
{
    public class OperationsTaskQueue : IOperationsTaskQueue
    {
        private readonly Queue<OperationDetails> _operations;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(1);
        private readonly ILogger<OperationsTaskQueue> _logger;

        public OperationsTaskQueue(ILogger<OperationsTaskQueue> logger)
        {
            _operations = new Queue<OperationDetails>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<OperationDetails?> DequeueAsync()
        {
            if (_operations.Count == 0)
            {
                _signal.Release();
                return null;
            }

            var operation = _operations.Dequeue();
            _logger.OperationDequeued(operation.Operation);

            _signal.Release();

            return await Task.FromResult(operation);
        }

        public async Task QueueAsync(OperationDetails operation)
        {
            await _signal.WaitAsync();

            _logger.OperationQueueing(operation.Operation);
            if (!_operations.Any(o => o.Operation == operation.Operation))
            {
                _operations.Enqueue(operation);
            }
        }
    }
}
