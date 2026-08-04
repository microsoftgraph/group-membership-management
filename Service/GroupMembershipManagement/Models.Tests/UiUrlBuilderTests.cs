// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.Helpers;

namespace Models.Tests
{
    [TestClass]
    public class UiUrlBuilderTests
    {
        [DataTestMethod]
        [DataRow("https://gmm.example.com", "https://gmm.example.com/ManageMembership")]
        [DataRow("https://gmm.example.com/", "https://gmm.example.com/ManageMembership")]
        [DataRow("https://gmm.example.com/app/", "https://gmm.example.com/app/ManageMembership")]
        [DataRow("https://gmm.example.com/app?foo=bar#frag", "https://gmm.example.com/app/ManageMembership")]
        public void BuildOnboardingUrl_ReturnsOnboardingPage(string uiUrl, string expected)
        {
            Assert.AreEqual(expected, UiUrlBuilder.BuildOnboardingUrl(uiUrl));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("not-a-url")]
        [DataRow("/relative/path")]
        [DataRow("ftp://gmm.example.com")]
        public void BuildOnboardingUrl_ReturnsEmpty_ForInvalidBaseUrl(string uiUrl)
        {
            Assert.AreEqual(string.Empty, UiUrlBuilder.BuildOnboardingUrl(uiUrl));
        }
    }
}
