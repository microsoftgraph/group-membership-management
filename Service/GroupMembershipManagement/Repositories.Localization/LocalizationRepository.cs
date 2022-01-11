// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Localization;
using Repositories.Contracts;
using System;

namespace Repositories.Localization
{
    public class LocalizationRepository : ILocalizationRepository
    {
        private readonly IStringLocalizer<LocalizationRepository> _localizer = null;
        public LocalizationRepository(IStringLocalizer<LocalizationRepository> localizer)
        {
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        public string TranslateSetting(string settingName, params string[] additionalParams)
        {
            return additionalParams == null
                    ? _localizer.GetString(settingName)
                    : _localizer.GetString(settingName, additionalParams);
        }
    }
}
