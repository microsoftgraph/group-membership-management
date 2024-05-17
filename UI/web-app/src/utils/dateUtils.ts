// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

const SQLMinDate = new Date(Date.UTC(1753, 0, 1));

export function formatLastRunTime(lastSuccessfulRunTime: string): [string, number] {
    const lastRunTimeDate = new Date(lastSuccessfulRunTime);
    const today = new Date();
    const hoursAgo = Math.round(Math.abs(today.getTime() - lastRunTimeDate.getTime()) / 36e5);

    const formattedDate: string = lastRunTimeDate.toLocaleDateString();

    if (
        lastRunTimeDate.getUTCFullYear() === SQLMinDate.getUTCFullYear() &&
        lastRunTimeDate.getUTCMonth() === SQLMinDate.getUTCMonth() &&
        lastRunTimeDate.getUTCDate() === SQLMinDate.getUTCDate()
    ) {
        return [SQLMinDate.toLocaleDateString(), 0];
    }

    return [formattedDate, hoursAgo];
}

export function formatNextRunTime(estimatedNextRunTime: string, enabled: boolean): [string, number] {
    const estimatedNextRunTimeDate = new Date(estimatedNextRunTime);
    const today = new Date();
    const hoursLeft = Math.round(Math.abs(estimatedNextRunTimeDate.getTime() - today.getTime()) / 36e5);

    const formattedDate: string = estimatedNextRunTimeDate.toLocaleDateString();

    if (!enabled) {
        return ['-', 0];
    } else {
        return [formattedDate, hoursLeft];
    }
}
