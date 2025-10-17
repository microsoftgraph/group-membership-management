// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { GroupSettings } from './GroupSettings';

export interface Destination {
  id?: string;
  name?: string;
  channelId?: string;
  channelName?: string;
  type: string;
  endpoints?: string[] | undefined;
  email?: string;
  groupSettings?: GroupSettings | undefined;
};