// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    IProcessedStyleSet,
    IconButton,
    TextField,
    Dropdown,
    IDropdownOption,
} from '@fluentui/react';

import {
    IPagingBarProps, IPagingBarStyleProps, IPagingBarStyles,
} from './PagingBar.types';
import { useStrings } from '../../localization';


const getClassNames = classNamesFunction<
  IPagingBarStyleProps,
  IPagingBarStyles
>();

export const PagingBarBase: React.FunctionComponent<IPagingBarProps> = (
    props: IPagingBarProps
) => {
    const { className, styles } = props;

    const classNames: IProcessedStyleSet<IPagingBarStyles> = getClassNames(
        styles,
        {
        className
        }
    );

    const strings = useStrings();
    const { pageSize, pageNumber, totalNumberOfPages, setPageSize, setPageNumber, setPageSizeCookie, getJobsByPage } = props;

    const pageSizeOptions: IDropdownOption[] = [
        { key: '10', text: '10' },
        { key: '20', text: '20' },
        { key: '30', text: '30' },
        { key: '40', text: '40' },
        { key: '50', text: '50' },
    ];

    const onPageSizeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
        if (item) {
            setPageSize(item.key.toString());
            setPageNumber(1);
            setPageSizeCookie(item.key.toString());
            getJobsByPage();
        }
    }

    const navigateToPage = (direction: number) => {
        if (pageNumber + direction === 0 || totalNumberOfPages === undefined || pageNumber + direction > totalNumberOfPages)
            return;

        let newPageNumber = pageNumber + direction;
        setPageNumber(newPageNumber);
        getJobsByPage();
    }

    const onPageNumberChanged = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined): void => {
        if (newValue === undefined || newValue === ''
            || isNaN(parseInt(newValue))
            || parseInt(newValue) <= 0
            || (totalNumberOfPages !== undefined && !isNaN(totalNumberOfPages) && parseInt(newValue) > totalNumberOfPages))
            return;

        setPageNumber(parseInt(newValue));
        getJobsByPage();
    }

    return (
        <div className={classNames.mainContainer}>
            <div className={classNames.divContainer}>
                <IconButton
                    iconProps={{ iconName: 'ChevronLeft' }}
                    title={strings.JobsList.PagingBar.previousPage as string}
                    onClick={() => navigateToPage(-1)}
                />
                <label>{strings.JobsList.PagingBar.previousPage}</label>
                <div className={classNames.divContainer}>
                    <label className={classNames.leftLabelMessage}>{strings.JobsList.PagingBar.page}</label>
                    <TextField
                        style={{ width: 55 }}
                        value={pageNumber.toString()}
                        onChange={onPageNumberChanged}
                    />
                    <label className={classNames.rightLabelMessage}>{strings.JobsList.PagingBar.of} {(totalNumberOfPages ? totalNumberOfPages : 1)}</label>
                </div>
                <label>{strings.JobsList.PagingBar.nextPage}</label>
                <IconButton
                    iconProps={{ iconName: 'ChevronRight' }}
                    title={strings.JobsList.PagingBar.nextPage as string}
                    onClick={() => navigateToPage(1)}
                />
            </div>
            <div className={classNames.divContainer}>
                <label className={classNames.leftLabelMessage}>{strings.JobsList.PagingBar.display}</label>
                <Dropdown
                    options={pageSizeOptions}
                    defaultSelectedKey={pageSize}
                    onChange={onPageSizeChanged}
                />
                <label className={classNames.rightLabelMessage}>{strings.JobsList.PagingBar.items}</label>
            </div>
        </div >
    )
}