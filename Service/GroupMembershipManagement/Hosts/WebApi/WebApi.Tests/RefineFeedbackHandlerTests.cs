// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;
using WebApi.Models;

namespace WebApi.Tests
{
    [TestClass]
    public class RefineFeedbackHandlerTests
    {
        private const string Feedback = "the exclusions are not clear. please explain why each one is needed";
        private const string RefinedText = "The exclusions are not clear. Please explain why each one is needed.";

        private Mock<IDatabaseSettingsRepository> _mockSettingsRepository = null!;
        private Mock<IFeedbackRefinementService> _mockRefinementService = null!;
        private CapturingLogger<RefineFeedbackHandler> _logger = null!;
        private RefineFeedbackHandler _handler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockSettingsRepository = new Mock<IDatabaseSettingsRepository>();
            _mockRefinementService = new Mock<IFeedbackRefinementService>();
            _logger = new CapturingLogger<RefineFeedbackHandler>();

            EnableFeature();

            _handler = new RefineFeedbackHandler(_logger, _mockSettingsRepository.Object, _mockRefinementService.Object);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("\t\r\n")]
        public async Task ExecuteAsync_WithEmptyOrWhitespaceFeedback_Returns400WithoutSettingsOrServiceCalls(string feedback)
        {
            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(feedback));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.InvalidRequest, response.ErrorCode);
            Assert.IsNull(response.RefinedText);
            _mockSettingsRepository.Verify(x => x.GetSettingByKeyAsync(It.IsAny<SettingKey>()), Times.Never);
            _mockRefinementService.Verify(x => x.RefineAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithFeedbackOverTheInputLimit_Returns400WithoutSettingsOrServiceCalls()
        {
            var oversized = new string('a', FeedbackRefinementLimits.MaxInputLength + 1);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(oversized));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.FeedbackTooLong, response.ErrorCode);
            Assert.IsNull(response.RefinedText);
            _mockSettingsRepository.Verify(x => x.GetSettingByKeyAsync(It.IsAny<SettingKey>()), Times.Never);
            _mockRefinementService.Verify(x => x.RefineAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithFeedbackExactlyAtTheInputLimit_IsRefined()
        {
            var atLimit = new string('a', FeedbackRefinementLimits.MaxInputLength);
            _mockRefinementService.Setup(x => x.RefineAsync(atLimit)).ReturnsAsync(RefinedText);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(atLimit));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(RefinedText, response.RefinedText);
            _mockRefinementService.Verify(x => x.RefineAsync(atLimit), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithMissingFeatureSetting_Returns503FeatureDisabledWithoutServiceCall()
        {
            _mockSettingsRepository
                .Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAIRejectionFeedbackRefinementEnabled))
                .ReturnsAsync((Setting)null!);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.FeatureDisabled, response.ErrorCode);
            _mockRefinementService.Verify(x => x.RefineAsync(It.IsAny<string>()), Times.Never);
        }

        [DataTestMethod]
        [DataRow("false")]
        [DataRow("")]
        [DataRow("not-a-boolean")]
        public async Task ExecuteAsync_WithDisabledFeatureSetting_Returns503FeatureDisabled(string settingValue)
        {
            SetFeatureSetting(settingValue);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.FeatureDisabled, response.ErrorCode);
            _mockRefinementService.Verify(x => x.RefineAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenSettingsReadFails_Returns503ServiceUnavailableWithoutServiceCall()
        {
            _mockSettingsRepository
                .Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAIRejectionFeedbackRefinementEnabled))
                .ThrowsAsync(new InvalidOperationException("database unavailable"));

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.ServiceUnavailable, response.ErrorCode);
            _mockRefinementService.Verify(x => x.RefineAsync(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithEnabledFeature_ReturnsExactServiceOutput()
        {
            SetupRefinement(RefinedText);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(RefinedText, response.RefinedText);
            Assert.IsNull(response.ErrorCode);
            _mockRefinementService.Verify(x => x.RefineAsync(Feedback), Times.Once);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task ExecuteAsync_WithEmptyModelOutput_Returns422InvalidRefinedText(string output)
        {
            SetupRefinement(output!);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.InvalidRefinedText, response.ErrorCode);
            Assert.IsNull(response.RefinedText);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithOversizedModelOutput_Returns422AndWithholdsOutput()
        {
            SetupRefinement(new string('a', FeedbackRefinementLimits.MaxRefinedTextLength + 1));

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.RefinedTextTooLong, response.ErrorCode);
            Assert.IsNull(response.RefinedText);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithOutputAtMaximumLength_Succeeds()
        {
            var output = new string('a', FeedbackRefinementLimits.MaxRefinedTextLength);
            SetupRefinement(output);

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(output, response.RefinedText);
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenServiceTimesOut_Returns408()
        {
            SetupRefinementThrows(new TimeoutException("OpenAI API call timed out"));

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.RequestTimeout, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.Timeout, response.ErrorCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenServiceUnavailable_Returns503()
        {
            SetupRefinementThrows(new FeedbackRefinementUnavailableException());

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.ServiceUnavailable, response.ErrorCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithUnexpectedFailure_Returns500()
        {
            SetupRefinementThrows(new InvalidOperationException("unexpected"));

            var response = await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual(RefineFeedbackErrorCodes.InternalError, response.ErrorCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_NeverLogsFeedbackOrModelOutput()
        {
            SetupRefinement(RefinedText);

            await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            foreach (var entry in _logger.Entries)
            {
                StringAssert.DoesNotMatch(entry.Message, new System.Text.RegularExpressions.Regex("exclusions"));
                StringAssert.DoesNotMatch(entry.Message, new System.Text.RegularExpressions.Regex("Please explain"));
            }
        }

        [TestMethod]
        public async Task ExecuteAsync_OnFailure_DoesNotAttachRawException()
        {
            SetupRefinementThrows(new InvalidOperationException("provider said something sensitive"));

            await _handler.ExecuteAsync(new RefineFeedbackRequest(Feedback));

            Assert.IsTrue(_logger.Entries.Count > 0);
            Assert.IsFalse(_logger.Entries.Any(e => e.Exception != null));
            Assert.IsFalse(_logger.Entries.Any(e => e.Message.Contains("provider said something sensitive")));
        }

        [TestMethod]
        public async Task ExecuteAsync_OnSuccess_LogsContentFreeOutcomeDimensions()
        {
            SetupRefinement(RefinedText);
            var request = new RefineFeedbackRequest(Feedback);

            await _handler.ExecuteAsync(request);

            var success = _logger.Entries.SingleOrDefault(e => e.EventId.Id == 91551);
            Assert.IsNotNull(success);
            StringAssert.Contains(success!.Message, request.InstanceId.ToString());
            StringAssert.Contains(success.Message, Feedback.Length.ToString());
            StringAssert.Contains(success.Message, RefinedText.Length.ToString());
        }

        private void EnableFeature() => SetFeatureSetting("true");

        private void SetFeatureSetting(string value)
        {
            _mockSettingsRepository
                .Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAIRejectionFeedbackRefinementEnabled))
                .ReturnsAsync(new Setting(Guid.NewGuid(), SettingKey.IsAIRejectionFeedbackRefinementEnabled, value));
        }

        private void SetupRefinement(string output)
        {
            _mockRefinementService
                .Setup(x => x.RefineAsync(It.IsAny<string>()))
                .ReturnsAsync(output);
        }

        private void SetupRefinementThrows(Exception exception)
        {
            _mockRefinementService
                .Setup(x => x.RefineAsync(It.IsAny<string>()))
                .ThrowsAsync(exception);
        }
    }

    /// <summary>
    /// Captures formatted log entries so tests can assert that no feedback content,
    /// model output, or raw exception detail is ever written on the refinement path.
    /// </summary>
    public class CapturingLogger<T> : ILogger<T>
    {
        public record LogEntry(EventId EventId, string Message, Exception? Exception);

        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId, formatter(state, exception), exception));
        }
    }
}
