// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingGroupCalculatorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly List<int> _groupSizes = new List<int> { 10, 50, 100, 500, 1000, 5000, 10000, 50000, 100000, 200000 };

        public LoadTestingGroupCalculatorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(LoadTestingGroupCalculatorFunction))]
        public async Task<LoadTestingGroupCalculatorResponse> GenerateGroup([ActivityTrigger] LoadTestingGroupCalculatorRequest request, ILogger log)
        {
            var runId = request.RunId;
            var numberOfUsers = request.NumberOfUsers;
            var numberOfGroups = request.NumberOfGroups;
            
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingGroupCalculatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var groupSizesAndCounts = new Dictionary<int, int>();

            // find largest group size that can be completely filled
            var maxGroupSize = _groupSizes.LastOrDefault(num => num < numberOfUsers);
            var maxGroupIndex = _groupSizes.IndexOf(maxGroupSize);

            // Get group size distribution sequence
            var sequence = DistinctFibonacciSequence(maxGroupIndex + 1);
            var sum = sequence.Sum();

            var totalGroupCount = 0;
            // Determine how many groups of each size to create
            for(var i = 0; maxGroupIndex - i > 0; i++)
            {
                // Round up to nearest whole number
                var groupCount = (int)(((decimal)sequence[i] / sum) * numberOfGroups);
                groupSizesAndCounts.Add(_groupSizes[maxGroupIndex-i], numberOfGroups);
                totalGroupCount += numberOfGroups;
            }

            // Smallest group size gets the remaining number of groups that need to be created to match the requested number of groups
            groupSizesAndCounts.Add(_groupSizes[0], numberOfGroups - totalGroupCount);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingGroupCalculatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new LoadTestingGroupCalculatorResponse
            {
                GroupSizesAndCounts = groupSizesAndCounts
            };
        }

        public List<int> DistinctFibonacciSequence(int sequenceLength)
        {
            List<int> sequence = new List<int>();
            int a = 1;
            int b = 2;

            while (sequence.Count <= sequenceLength)
            {
                sequence.Add(a);
                int c = a + b;
                a = b;
                b = c;
            }

            return sequence;
        }
    }
}
