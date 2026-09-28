// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ColumnActionsMode } from '@fluentui/react';

export const getDestinationTypeColumnActionsMode = (
  isTenantJobWriter?: boolean,
  isTeamsChannelOnboarder?: boolean
): ColumnActionsMode =>
  isTenantJobWriter || isTeamsChannelOnboarder
    ? ColumnActionsMode.hasDropdown
    : ColumnActionsMode.disabled;
