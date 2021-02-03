// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts
{
    public interface ILocalizationRepository
    {
        string TranslateSetting(string settingName, params string[] additionalParams);
    }
}
