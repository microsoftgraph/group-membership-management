// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as signalR from '@microsoft/signalr';
import { ISignalRService } from './ISignalRService';
import { store } from '../../store';
import { updateServiceStatus } from '../../store/operations.slice';
import { ServiceStatuses } from '../../models';

export class SignalRStatusService implements ISignalRService {
    private _connection: signalR.HubConnection | null = null;

    public startConnection() {
        if (!this._connection
            || (this._connection && (this._connection.state !== signalR.HubConnectionState.Connected
                                        && this._connection.state !== signalR.HubConnectionState.Connecting))) {
            this._connection = new signalR.HubConnectionBuilder()
                .withUrl(`${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/servicestatus`)
                .withAutomaticReconnect()
                .build();

             const startPromise = this._connection.start()
                .then(() => console.log('SignalR Connected'))
                .catch((err: any) => console.error('SignalR Connection Error:', err));

            this._connection.on("ServiceStatusChanged", (status: ServiceStatuses) => {
                console.log("Service status changed:", status);
                store.dispatch(updateServiceStatus(status));
            });

            return startPromise;
        } else
            return Promise.resolve();
    }

    public stopConnection() {
        if (this._connection) {
            this._connection.stop();
            this._connection = null;
        }
    }
}
