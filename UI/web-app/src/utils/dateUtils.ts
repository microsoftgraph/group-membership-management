// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import moment from 'moment';
import { useStrings } from '../store/hooks';

export function formatLastRunTime(lastSuccessfulRunTime: string, period: number): [string, number] {
    // eslint-disable-next-line react-hooks/rules-of-hooks
    const strings = useStrings();
    const currentTime = moment.utc();
    const lastRunMoment = moment.utc(lastSuccessfulRunTime);
    const SQL_MIN_DATE = new Date('1753-01-01T00:00:00');
    const hoursAgo = currentTime.diff(lastRunMoment, 'hours');
    const formattedDate = lastRunMoment.local().format('MM/DD/YYYY');

    if (lastRunMoment.isSame(SQL_MIN_DATE)) {
        return [strings.pendingInitialSync, 0];  
    }

    if (hoursAgo > period) {
        return [formattedDate, period];
    } else {
        return [formattedDate, hoursAgo];
    }
}


export function formatNextRunTime(lastSuccessfulRunTime: string, period: number, enabled: boolean): [string, number] {
    const currentTime = moment.utc();
    const lastRunMoment = moment.utc(lastSuccessfulRunTime);

    const estimatedNextRunTime = lastRunMoment.add(period, 'hours');
    const formattedDate = estimatedNextRunTime.local().format('MM/DD/YYYY');
    const isNextRunInThePast = estimatedNextRunTime.isBefore(currentTime);
    const hoursLeft = isNextRunInThePast ? 0 : Math.abs(currentTime.diff(estimatedNextRunTime, 'hours'));

    if (isNextRunInThePast || !enabled) {
        return ['', 0];
    } else {
        return [formattedDate, hoursLeft];
    }
}
