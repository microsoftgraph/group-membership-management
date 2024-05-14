// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import moment from 'moment';
const SQLMinDateMoment = moment.utc('1753-01-01T00:00:00');

export function formatLastRunTime(lastSuccessfulRunTime: string): [string, number] {
    const lastRunTimeMoment = moment.utc(lastSuccessfulRunTime);
    const today = moment.utc();
    const hoursAgo = Math.round(Math.abs(today.valueOf() - lastRunTimeMoment.valueOf()) / 36e5);

    const formattedDate: string = lastRunTimeMoment.format('MM/DD/YYYY');

    if (lastRunTimeMoment.isSame(SQLMinDateMoment, 'day')) {
        return [SQLMinDateMoment.toLocaleString(), 0];
    }

    return [formattedDate, hoursAgo];
}

export function formatNextRunTime(estimatedNextRunTime: string, enabled: boolean): [string, number] {
    const estimatedNextRunTimeMoment = moment.utc(estimatedNextRunTime);
    const today = moment.utc();
    const hoursLeft = Math.round(Math.abs(today.valueOf() - estimatedNextRunTimeMoment.valueOf()) / 36e5);
    
    const formattedDate: string = estimatedNextRunTimeMoment.local().format('MM/DD/YYYY');

    if (!enabled) {
        return ['-', 0];
    }
    else {
        return [formattedDate, hoursLeft];
    }
}