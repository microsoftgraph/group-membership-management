// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models.Entities;
using Repositories.Contracts;
using Services;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Runtime.CompilerServices;

namespace WebApi.Configuration
{
    public static class MessageHandlerInjector
    {
        public static void InjectMessageHandlers(WebApplicationBuilder builder)
        {

            builder.Services.AddSingleton<ILoggingRepository>(x => new ConsoleLoggingRepository()); // TODO: remove this, once the real ILoggingRepository gets added

            builder.Services.AddTransient<IRequestHandler<GetJobsRequest, GetJobsResponse>, GetJobsHandler>();
        }
    }

    // TODO: remove this, once the real ILoggingRepository gets added
    public class ConsoleLoggingRepository : ILoggingRepository
    {
        public Dictionary<Guid, LogProperties> SyncJobProperties => throw new NotImplementedException();

        public bool DryRun { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        Dictionary<Guid, LogProperties> ILoggingRepository.SyncJobProperties => throw new NotImplementedException();

        public Task LogMessageAsync(LogMessage logMessage, VerbosityLevel verbosityLevel = VerbosityLevel.INFO, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            Console.WriteLine($"[{verbosityLevel}] - {caller} - {logMessage}");
            return Task.CompletedTask;
        }

        public Task LogPIIMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            throw new NotImplementedException();
        }

        public void RemoveSyncJobProperties(Guid key)
        {
            throw new NotImplementedException();
        }

        public void SetSyncJobProperties(Guid key, Dictionary<string, string> properties)
        {
            throw new NotImplementedException();
        }
    }
}
