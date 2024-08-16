// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as signalR from '@microsoft/signalr';
import { store } from '../../store';
import { updateServiceStatus } from '../../store/operations.slice';
import { ServiceStatuses } from '../../models';

const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://gmm-compute-ag-signalr.service.signalr.net") // Change this to be configurable
    .build();

connection.start()
    .then(() => console.log('SignalR Connected'))
    .catch((err: any) => console.error('SignalR Connection Error:', err));

connection.on("ServiceStatusChanged", (status: ServiceStatuses) => {
    console.log("Service status changed:", status);
    store.dispatch(updateServiceStatus(status));
});
