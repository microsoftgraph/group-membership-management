// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DestinationType } from '../models/DestinationType';
import { strings } from '../services/localization/i18n/locales/en/translations';

export const destinationTypeLocalization: Record<string, string> = {
    [DestinationType.GroupMembership.toString()]: strings.JobsList.JobsListFilter.filters.destinationType.options.group,
    [DestinationType.TeamsChannelMembership.toString()]: strings.JobsList.JobsListFilter.filters.destinationType.options.channel
};