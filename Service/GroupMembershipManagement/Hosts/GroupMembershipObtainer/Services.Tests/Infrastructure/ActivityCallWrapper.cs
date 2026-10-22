// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.DurableTask;
using System;
using System.Threading.Tasks;

namespace Tests.Services.Infrastructure
{
    internal static class ActivityCallWrapper
    {
        public static async Task<TResult> InvokeAsync<TResult>(
            string activityName,
            Func<Task<TResult>> call)
        {
            try
            {
                return await call();
            }
            catch (TaskFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new TaskFailedException(
                    activityName,
                    -1,
                    TaskFailureDetails.FromException(exception));
            }
        }

        public static async Task InvokeAsync(
            string activityName,
            Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (TaskFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new TaskFailedException(
                    activityName,
                    -1,
                    TaskFailureDetails.FromException(exception));
            }
        }
    }
}
