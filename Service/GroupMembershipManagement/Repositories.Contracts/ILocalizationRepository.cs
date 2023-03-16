// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Repositories.Contracts
{
    public interface ILocalizationRepository
    {
        string TranslateSetting(string settingName, params string[] additionalParams);
        string TranslateSetting(Enum enumValue, params string[] additionalParams);
    }
}
