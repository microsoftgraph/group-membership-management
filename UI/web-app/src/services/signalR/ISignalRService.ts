// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ISignalRService {
    startConnection: () => void;
    stopConnection: () => void;
};