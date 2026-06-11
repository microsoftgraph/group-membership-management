// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics;
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

        [TestMethod]
        public void TelemetryInitializerTagsFunctionUserCategoryFromCategoryProperty()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["Category"] = "Function.JobUpdaterFunction.User";

            initializer.Initialize(telemetry);

            Assert.AreEqual(TelemetryConstants.LogSource, telemetry.Properties[TelemetryConstants.LogSourceProperty]);
        }

        [TestMethod]
        public void TelemetryInitializerDoesNotTagFunctionInfrastructureCategory()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["Category"] = "Function.JobUpdaterFunction";

            initializer.Initialize(telemetry);

            Assert.IsFalse(telemetry.Properties.ContainsKey(TelemetryConstants.LogSourceProperty));
        }

        [TestMethod]
        public void TelemetryInitializerCopiesCorrelationPropertiesFromCurrentActivity()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            var activity = new Activity("test");

            activity.Start();
            activity.SetTag("RunId", "run-123");
            activity.AddBaggage("SyncJobId", "sync-456");
            try
            {
                initializer.Initialize(telemetry);
            }
            finally
            {
                activity.Stop();
            }

            Assert.AreEqual("run-123", telemetry.Properties["RunId"]);
            Assert.AreEqual("sync-456", telemetry.Properties["SyncJobId"]);
            Assert.IsFalse(telemetry.Properties.ContainsKey("MembershipType"));
        }

        [TestMethod]
        public void TelemetryInitializerDoesNotOverrideExistingCorrelationProperties()
        {
            var initializer = new TelemetryInitializer(new TelemetryInitializerConfig());
            var telemetry = new TraceTelemetry("test trace");
            telemetry.Properties["RunId"] = "existing-run";
            var activity = new Activity("test");

            activity.Start();
            activity.SetTag("RunId", "activity-run");
            activity.SetTag("SyncJobId", "sync-456");

            try
            {
                initializer.Initialize(telemetry);
            }
            finally
            {
                activity.Stop();
            }

            Assert.AreEqual("existing-run", telemetry.Properties["RunId"]);
            Assert.AreEqual("sync-456", telemetry.Properties["SyncJobId"]);
        }
    }
}
