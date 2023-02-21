// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts.AzureMaintenance;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Mail;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Graph;

namespace Services.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
        [TestMethod]
        public async Task TestSendEmail()
        {
            var loggerMock = new Mock<ILoggingRepository>();
            loggerMock.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.INFO, It.IsAny<string>(), It.IsAny<string>()));

            var mailAddresses = new Mock<IEmailSenderRecipient>();
            var mailRespository = new Mock<IMailRepository>();

            var notifierService = new NotifierService(loggerMock.Object,
                                                mailRespository.Object,
                                                mailAddresses.Object
                                                );

            await notifierService.SendEmailAsync("user1@test.com;user2@test.com");
            mailRespository.Verify(x => x.SendMailAsync(It.IsAny<EmailMessage>(), null), Times.Once());
        }

    }
}
