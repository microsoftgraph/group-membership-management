// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Models.Tests
{
    [TestClass]
    public class RunLimiterSettingsTests
    {
        [TestMethod]
        public void MaxPendingAgeMinutes_DefaultsToSixty_WhenUnset()
        {
            var settings = new RunLimiterSettings();

            Assert.AreEqual(60, settings.MaxPendingAgeMinutes);
        }

        [DataTestMethod]
        [DataRow(1, 1)]
        [DataRow(30, 30)]
        [DataRow(120, 120)]
        [DataRow(0, 60)]
        [DataRow(-5, 60)]
        public void MaxPendingAgeMinutes_HonorsPositiveValues_AndFallsBackOtherwise(int configured, int expected)
        {
            var settings = new RunLimiterSettings { MaxPendingAgeMinutes = configured };

            Assert.AreEqual(expected, settings.MaxPendingAgeMinutes);
        }
    }
}
