// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DIConcreteTypes.Tests
{
    [TestClass]
    public class TelemetryInitializerTests
    {
        [DataTestMethod]
        [DataRow("Services.ResolveNotificationHandler")]
        [DataRow("Repositories.GraphGroupRepository")]
        [DataRow("Hosts.GraphUpdater.GraphUpdaterFunction")]
        public void TelemetryInitializerTagsAllowedCategoryAsGmm(string categoryName)
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["CategoryName"] = categoryName;

            initializer.Initialize(telemetry);

            Assert.AreEqual(TelemetryConstants.LogSource, telemetry.Properties[TelemetryConstants.LogSourceProperty]);
        }

        [TestMethod]
        public void TelemetryInitializerDoesNotTagCategoryOutsideAllowedPrefixes()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["CategoryName"] = "Microsoft.Azure.Functions.Worker";

            initializer.Initialize(telemetry);

            Assert.IsFalse(telemetry.Properties.ContainsKey(TelemetryConstants.LogSourceProperty));
        }

        [TestMethod]
        public void TelemetryInitializerDoesNotTagTelemetryWithoutCategoryName()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");

            initializer.Initialize(telemetry);

            Assert.IsFalse(telemetry.Properties.ContainsKey(TelemetryConstants.LogSourceProperty));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("   ")]
        public void TelemetryInitializerDoesNotTagTelemetryWithEmptyOrWhitespaceCategoryName(string categoryName)
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["CategoryName"] = categoryName;

            initializer.Initialize(telemetry);

            Assert.IsFalse(telemetry.Properties.ContainsKey(TelemetryConstants.LogSourceProperty));
        }

        [DataTestMethod]
        [DataRow("Worker.Grpc")]
        [DataRow("Host.Triggers.Timer")]
        [DataRow("ServicesCustom.Handler")]
        public void TelemetryInitializerDoesNotTagNearMissOrExcludedCategory(string categoryName)
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["CategoryName"] = categoryName;

            initializer.Initialize(telemetry);

            Assert.IsFalse(telemetry.Properties.ContainsKey(TelemetryConstants.LogSourceProperty));
        }

        [TestMethod]
        public void TelemetryInitializerTagsCategoryUsingAdditionalAllowedPrefixes()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig
            {
                AdditionalAllowedPrefixes = ["Custom.Namespace."]
            });
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["CategoryName"] = "Custom.Namespace.Handler";

            initializer.Initialize(telemetry);

            Assert.AreEqual(TelemetryConstants.LogSource, telemetry.Properties[TelemetryConstants.LogSourceProperty]);
        }
    }
}
