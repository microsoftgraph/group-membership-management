// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.AutoApprover;

namespace Services.Tests
{
    [TestClass]
    public class AutoApproverServiceTests
    {
        [TestMethod]
        public void TestServiceInstantiation()
        {
            var service = new AutoApproverService();
            Assert.IsNotNull(service);
        }
    }
}
