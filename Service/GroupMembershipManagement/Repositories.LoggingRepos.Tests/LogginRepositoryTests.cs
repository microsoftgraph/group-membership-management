// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Repositories.LoggingRepos.Tests
{
	[TestClass]
	public class LogginRepositoryTests
	{
		[TestMethod]
		public async Task CheckLoggingRepositoryResponseForStatusCodeOK()
		{
			var mockLoggingRepository = new MockLoggingRepository();
			mockLoggingRepository.StatusCode = HttpStatusCode.OK;

			await mockLoggingRepository.LogMessageAsync(new LogMessage { Message = $"Test log message.", RunId = Guid.NewGuid() });

			Assert.AreEqual(mockLoggingRepository.FinalStatusCode, HttpStatusCode.OK);
		}

		[TestMethod]
		public async Task CheckLoggingRepositoryResponseForStatusCodeServiceUnavailable()
		{
			var mockLoggingRepository = new MockLoggingRepository();
			mockLoggingRepository.StatusCode = HttpStatusCode.ServiceUnavailable;

			await mockLoggingRepository.LogMessageAsync(new LogMessage { Message = $"Test log message.", RunId = Guid.NewGuid() });

			Assert.AreEqual(mockLoggingRepository.FinalStatusCode, HttpStatusCode.ServiceUnavailable);
		}

		[TestMethod]
		public async Task CheckLoggingRepositoryResponseForRetryPolicy()
		{
			var mockLoggingRepository = new MockLoggingRepository();
			mockLoggingRepository.StatusCode = HttpStatusCode.ServiceUnavailable;
			mockLoggingRepository.PollyPolicySucceeds = true;

			await mockLoggingRepository.LogMessageAsync(new LogMessage { Message = $"Test log message.", RunId = Guid.NewGuid() });

			Assert.AreEqual(mockLoggingRepository.FinalStatusCode, HttpStatusCode.OK);
		}
	}
}
