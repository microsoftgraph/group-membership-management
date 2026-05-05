// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlDataChecker.Tests
{
    [TestClass]
    public class SqlDataCheckerValidatorServiceTests
    {

        [TestMethod]
        public async Task ValidateColumn_NullsBelowThreshold_DoesNotThrow()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 10 },
                    { "Column2", 5 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 8 },
                    { "Column2", 4 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // 10/100 = 10%, 5/100 = 5% — both well below the 50% default threshold
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_NullsExceedDefaultThreshold_ThrowsInvalidOperationException()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 60 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 5 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // 60/100 = 60% > 50% default threshold
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => InvokeValidateColumn(request));

            Assert.IsTrue(ex.Message.Contains("Column1"));
            Assert.IsTrue(ex.Message.Contains("FAILED"));
            Assert.IsTrue(ex.Message.Contains("threshold: 50%"));
        }

        [TestMethod]
        public async Task ValidateColumn_NullsExactlyAtThreshold_DoesNotThrow()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 50 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 50 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // 50/100 = 50%, exactly at threshold — should NOT throw (> not >=)
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_PerColumnThresholdOverridesDefault()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 30 },
                    { "Column2", 30 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 10 },
                    { "Column2", 10 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = new Dictionary<string, double>
                {
                    { "Column1", 0.20 } // 20% threshold for Column1 only
                }
            };

            // Column1: 30% > 20% custom threshold → should fail
            // Column2: 30% < 50% default threshold → fine
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => InvokeValidateColumn(request));

            Assert.IsTrue(ex.Message.Contains("Column1"));
            Assert.IsTrue(ex.Message.Contains("threshold: 20%"));
            Assert.IsFalse(ex.Message.Contains("Column2"));
        }

        [TestMethod]
        public async Task ValidateColumn_ColumnInLatestButNotPrevious_DefaultsPreviousToZero()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "NewColumn", 10 }
                },
                PreviousNullColumns = new Dictionary<string, int>(), // NewColumn not present
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // 10/100 = 10% — below 50% threshold, should not throw
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_LatestColumnsNull_SkipsThresholdCheck()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = null,
                PreviousNullColumns = new Dictionary<string, int> { { "Column1", 5 } },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // No latest data — should skip and not throw
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_ZeroLatestRows_SkipsThresholdCheck()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int> { { "Column1", 50 } },
                PreviousNullColumns = new Dictionary<string, int> { { "Column1", 5 } },
                LatestNumberOfRows = 0,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // Zero rows — should skip threshold check
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_MultipleColumnsExceedThreshold_ReportsAll()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 60 },
                    { "Column2", 70 },
                    { "Column3", 10 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 5 },
                    { "Column2", 5 },
                    { "Column3", 5 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // Column1: 60% > 50%, Column2: 70% > 50%, Column3: 10% < 50%
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => InvokeValidateColumn(request));

            Assert.IsTrue(ex.Message.Contains("Column1"));
            Assert.IsTrue(ex.Message.Contains("Column2"));
            Assert.IsFalse(ex.Message.Contains("Column3"));
            Assert.IsTrue(ex.Message.Contains("2 column(s)"));
        }

        [TestMethod]
        public async Task ValidateColumn_NullThresholds_UsesDefaultForAll()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 40 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 5 }
                },
                LatestNumberOfRows = 100,
                PreviousNumberOfRows = 100,
                ColumnThresholds = null
            };

            // 40% < 50% default — should not throw
            await InvokeValidateColumn(request);
        }

        [TestMethod]
        public async Task ValidateColumn_FractionalThreshold_PrecisionPreserved()
        {
            var request = new DifferenceCheckerRequest
            {
                LatestNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 5001 }
                },
                PreviousNullColumns = new Dictionary<string, int>
                {
                    { "Column1", 0 }
                },
                LatestNumberOfRows = 10000,
                PreviousNumberOfRows = 10000,
                ColumnThresholds = new Dictionary<string, double>
                {
                    { "Column1", 0.5001 } // 50.01% threshold
                }
            };

            // 5001/10000 = 50.01%, exactly at threshold — should NOT throw (strict >)
            await InvokeValidateColumn(request);
        }

        /// <summary>
        /// Helper to invoke the DifferenceCheckerFunction.ValidateColumn method
        /// without needing Durable Functions infrastructure.
        /// We construct the function with mocked dependencies and call the method directly.
        /// </summary>
        private async Task InvokeValidateColumn(DifferenceCheckerRequest request)
        {
            var mockLogging = new Mock<ILoggingRepository>();
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration { DisableTelemetry = true });

            var mockKvSecret = new Mock<Repositories.Contracts.InjectConfig.IKeyVaultSecret<SqlDataCheckerValidatorService>>();
            mockKvSecret.Setup(x => x.Secret).Returns("Server=fake;Database=fake;");
            var mockDataFactory = new Mock<Repositories.Contracts.IDataFactoryRepository>();

            var validatorService = new SqlDataCheckerValidatorService(
                mockLogging.Object,
                telemetryClient,
                mockKvSecret.Object,
                mockDataFactory.Object);

            var function = new DifferenceCheckerFunction(
                validatorService,
                mockLogging.Object,
                telemetryClient);

            await function.ValidateColumn(request);
        }
    }
}
