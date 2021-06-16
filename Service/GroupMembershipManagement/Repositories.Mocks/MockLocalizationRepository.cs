// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockLocalizationRepository : ILocalizationRepository
    {
        public string TranslateSetting(string settingName, params string[] additionalParams)
        {
            return "example string";
        }        
    }
}
