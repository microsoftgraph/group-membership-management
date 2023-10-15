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

        /// <summary>
        /// Creates a dictionary of group sizes and the number of groups of that size to create.
        /// It attempts to create many more smaller groups than larger groups to more closely resemble production usage.
        /// </summary>
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
            var maxGroupSizeIndex = _groupSizes.IndexOf(maxGroupSize);

            // Get group size distribution sequence
            var sequence = DistinctFibonacciSequence(maxGroupSizeIndex + 1);
            var sum = sequence.Sum();

            var totalGroupCount = 0;
            // Determine how many groups of each size to create
            for(var i = 0; maxGroupSizeIndex - i > 0; i++)
            {
                // Round up to nearest whole number
                var groupCount = (int)(((decimal)sequence[i] / sum) * numberOfGroups);
                // Small values for "numberOfGroups" with a large value for "numberOfUsers" can result
                // in the groups not being created for the larger group sizes.It's better to not include
                // a group size if the group count is 0.
                if (groupCount > 0) {
                    totalGroupCount += groupCount;
                    groupSizesAndCounts.Add(_groupSizes[maxGroupSizeIndex-i], groupCount);
                }    
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
            // First two numbers in the sequence are always 1, so by setting b to 2, we can skip the first iteration.
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
