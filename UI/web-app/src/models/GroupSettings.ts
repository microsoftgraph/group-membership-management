// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AuthorizedSender } from './AuthorizedSender';

export type GroupSettings = {
    authorizedSenders?: AuthorizedSender[];
    hiddenFromExchangeClients?: boolean;
    welcomeMessageEnabled?: boolean;
};