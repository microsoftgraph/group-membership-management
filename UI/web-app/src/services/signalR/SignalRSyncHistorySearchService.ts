// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as signalR from '@microsoft/signalr';
import { SyncHistorySearchProgressUpdate } from '../../models/SyncHistorySearchProgressUpdate';

export class SignalRSyncHistorySearchService {
    private _connection: signalR.HubConnection | null = null;
    private _handler: ((update: SyncHistorySearchProgressUpdate) => void) | null = null;

    public async startConnection(): Promise<void> {
        if (!this._connection
            || (this._connection
                && this._connection.state !== signalR.HubConnectionState.Connected
                && this._connection.state !== signalR.HubConnectionState.Connecting)) {
            this._connection = new signalR.HubConnectionBuilder()
                .withUrl(`${process.env.REACT_APP_AAD_APP_SERVICE_BASE_URI}/servicestatus`)
                .withAutomaticReconnect()
                .build();

            await this._connection.start();
        }

        if (this._connection && !this._handler) {
            this._handler = (update: SyncHistorySearchProgressUpdate) => {
                if (this.onProgress) {
                    this.onProgress(update);
                }
            };
            this._connection.on('SyncHistorySearchProgress', this._handler);
        }
    }

    public async subscribe(requestId: string): Promise<void> {
        if (!this._connection || this._connection.state !== signalR.HubConnectionState.Connected) {
            await this.startConnection();
        }

        await this._connection?.invoke('SubscribeToSyncHistorySearchProgress', requestId);
    }

    public async unsubscribe(requestId: string): Promise<void> {
        if (!this._connection || this._connection.state !== signalR.HubConnectionState.Connected) {
            return;
        }

        await this._connection.invoke('UnsubscribeFromSyncHistorySearchProgress', requestId);
    }

    public stopConnection(): void {
        if (this._connection) {
            if (this._handler) {
                this._connection.off('SyncHistorySearchProgress', this._handler);
            }
            this._connection.stop();
            this._connection = null;
            this._handler = null;
        }
    }

    public onProgress: ((update: SyncHistorySearchProgressUpdate) => void) | null = null;
}