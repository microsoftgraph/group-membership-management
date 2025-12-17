// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;

namespace Services.Tests
{
    [TestClass]
    public class LoggerFunctionTests
    {
        [TestMethod]
        public async Task LogMessageAsync_ForwardsToLoggingRepository()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var function = new LoggerFunction(loggingRepository.Object);

            var request = new LoggerRequest
            {
                Message = new LogMessage { Message = "hello", RunId = Guid.NewGuid() },
                Verbosity = VerbosityLevel.DEBUG
            };

            await function.LogMessageAsync(request);

            loggingRepository.Verify(x => x.LogMessageAsync(request.Message, request.Verbosity, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }
    }
}
