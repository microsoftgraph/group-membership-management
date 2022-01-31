// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AzureUserReader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Services.Tests
{
    [TestClass]
    public class PasswordGeneratorTests
    {
        private char[] _specialCharacters = "@#$%^&*-_!+=[]{}| \\:',.?/`~\"()".ToCharArray();
        [TestMethod]
        public void GenerateValidPasswords()
        {
            for (int i = 0; i < 10000; i++)
            {
                var password = PasswordGenerator.GeneratePassword();

                Assert.IsTrue(password.Any(x => char.IsUpper(x)));
                Assert.IsTrue(password.Any(x => char.IsLower(x)));
                Assert.IsTrue(password.Any(x => char.IsNumber(x)));
                Assert.IsTrue(password.Any(x => _specialCharacters.Contains(x)));
            }
        }
    }
}
