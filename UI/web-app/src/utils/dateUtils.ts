// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import moment from 'moment';
const SQLMinDateMoment = moment.utc('1753-01-01T00:00:00');

export function formatLastRunTime(lastSuccessfulRunTime: string): [string, number] {
    const lastRunMoment = moment.utc(lastSuccessfulRunTime);

    const lastRunTime = new Date(lastSuccessfulRunTime);
    const today = new Date();
    const hoursAgo = Math.round(Math.abs(today.valueOf() - lastRunTime.valueOf()) / 36e5);

    const formattedDate = lastRunMoment.format('MM/DD/YYYY');

    if (lastRunMoment.isSame(SQLMinDateMoment, 'day')) {
        return [SQLMinDateMoment.toLocaleString(), 0];
    }

    return [formattedDate, hoursAgo];
}

export function formatNextRunTime(lastSuccessfulRunTime: string, period: number, enabled: boolean): [string, number] {
    let lastRunMoment = moment.utc(lastSuccessfulRunTime);
    let estimatedNextRunTime = lastRunMoment.add(period, 'hours');

    const formattedDate: string = estimatedNextRunTime.local().format('MM/DD/YYYY');
    const lastRunTime = new Date(lastSuccessfulRunTime);
    const today = new Date();
    const hoursLeft = Math.round(Math.abs(today.valueOf() - lastRunTime.valueOf()) / 36e5);

    if (!enabled) {
        return ['-', 0];
    }
    else {
        return [formattedDate, hoursLeft];
    }
}