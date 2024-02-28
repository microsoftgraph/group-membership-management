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
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { selectPagingBarPageNumber, selectPagingBarPageSize, selectPagingBarTotalNumberOfPages, setPageNumber, setPageSize } from '../../store/pagingBar.slice';
import { AppDispatch } from '../../store';

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

    const dispatch = useDispatch<AppDispatch>();
    const strings = useStrings();
    const pageSize: string = useSelector(selectPagingBarPageSize);
    const pageNumber: number = useSelector(selectPagingBarPageNumber);
    const totalNumberOfPages: number = useSelector(selectPagingBarTotalNumberOfPages);

    const pageSizeOptions: IDropdownOption[] = [
        { key: '10', text: '10' },
        { key: '20', text: '20' },
        { key: '30', text: '30' },
        { key: '40', text: '40' },
        { key: '50', text: '50' },
    ];

    const onPageSizeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
        if (item) {
            dispatch(setPageSize(item.key.toString()));
            dispatch(setPageNumber(pageNumber));
        }
    }

    const navigateToPage = (direction: number) => {
        if (pageNumber + direction === 0 || totalNumberOfPages === undefined || pageNumber + direction > totalNumberOfPages)
            return;

        let newPageNumber = pageNumber + direction;
        dispatch(setPageNumber(newPageNumber));
    }

    const onPageNumberChanged = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined): void => {
        if (newValue === undefined || newValue === ''
            || isNaN(parseInt(newValue))
            || parseInt(newValue) <= 0
            || (totalNumberOfPages !== undefined && !isNaN(totalNumberOfPages) && parseInt(newValue) > totalNumberOfPages))
            return;
        
        dispatch(setPageNumber(parseInt(newValue)));
    }

    return (
        <div className={classNames.mainContainer}>
            <div className={classNames.divContainer}>
                <IconButton
                    iconProps={{ iconName: 'ChevronLeft' }}
                    title={strings.JobsList.PagingBar.previousPage as string}
                    onClick={() => navigateToPage(-1)}
                    disabled={totalNumberOfPages === undefined || pageNumber <= 1}
                />
                <label>{strings.JobsList.PagingBar.previousPage}</label>
                <div className={classNames.divContainer}>
                    <label className={classNames.leftLabelMessage}>{strings.JobsList.PagingBar.page}</label>
                    <TextField
                        ariaLabel={strings.JobsList.PagingBar.pageNumberAriaLabel}
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
                    disabled={totalNumberOfPages === undefined || pageNumber >= totalNumberOfPages}
                />
            </div>
            <div className={classNames.divContainer}>
                <label className={classNames.leftLabelMessage}>{strings.JobsList.PagingBar.display}</label>
                <Dropdown
                    title={strings.JobsList.PagingBar.pageSizeAriaLabel}
                    options={pageSizeOptions}
                    defaultSelectedKey={pageSize}
                    onChange={onPageSizeChanged}
                />
                <label className={classNames.rightLabelMessage}>{strings.JobsList.PagingBar.items}</label>
            </div>
        </div >
    )
}