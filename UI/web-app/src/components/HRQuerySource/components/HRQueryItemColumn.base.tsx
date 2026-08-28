// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import {
  ActionButton,
  ComboBox,
  Dropdown,
  Label,
  Spinner,
  SpinnerSize,
  Stack,
  Text,
  TextField,
  VirtualizedComboBox,
  classNamesFunction,
  type IProcessedStyleSet,
  type IComboBox,
  type IComboBoxOption,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { useStrings } from '../../../store/hooks';
import { nullOptions, getOrAndOperatorOptions } from '../../../models/Options';
import type {
  HRQueryItemColumnProps,
  HRQueryItemColumnStyleProps,
  HRQueryItemColumnStyles,
} from './HRQueryItemColumn.types';

export const getClassNames = classNamesFunction<HRQueryItemColumnStyleProps, HRQueryItemColumnStyles>();

export const HRQueryItemColumnBase: React.FunctionComponent<HRQueryItemColumnProps> = (props: HRQueryItemColumnProps) => {
  const {
    className,
    styles,
    items,
    item,
    index,
    column,
    groupIndex,
    childIndex,
    attributes,
    attributeMappings,
    areAttributeMappingsLoading,
    groupingEnabled,
    groups,
    groupIndexForAttribute,
    childIndexForAttribute,
    itemIndexForAttribute,
    groupIndexForAttributeValue,
    childIndexForAttributeValue,
    itemIndexForAttributeValue,
    filteredOptions,
    filteredValueOptions,
    isJobWriter,
    isEditable,
    displayEmail,
    onUpClick,
    onDownClick,
    onAttributeChange,
    handleAttributeChange,
    handleEqualityOperatorChange,
    handleAttributeValueChange,
    handleTAttributeValueChange,
    handleBlur,
    handleOrAndOperatorChange,
    removeComponent,
    onAttributeValueChange,
    getOptions,
    getValueOptions,
    getSelectedKeys,
    checkType,
    getValidOperatorsForType,
    onRenderAttributeComboBoxOptions,
    onRenderValueComboBoxOptions,
    onRenderValueComboBoxList,
    onRenderOperatorOptions,
  } = props;

  const classNames: IProcessedStyleSet<HRQueryItemColumnStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const strings = useStrings();
  const orAndOperatorOptions = getOrAndOperatorOptions(strings);
  const [isFocused, setIsFocused] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const valueCbRef = React.useRef<IComboBox | null>(null);
  const [shouldReopen, setShouldReopen] = useState(false); // reopen only for search-driven picks

  if (typeof index !== 'undefined' && items[index]) {
    const currentAttributeKey = items[index].attribute;
    const attribute = attributes?.find(
      (attr) =>
        (attr.hasMapping ? `${attr.name}_Code` : attr.name) === currentAttributeKey
    );
    const isAttributeDisabled = attribute?.enabled === false;

    const attributeOptions = groupingEnabled
      ? (groups.length > 0 &&
          (groupIndex === undefined && groupIndexForAttribute === -1 ? true : groupIndex === groupIndexForAttribute) &&
          (childIndex === undefined && childIndexForAttribute === -1 ? true : childIndex === childIndexForAttribute) &&
          (index === undefined && itemIndexForAttribute === -1 ? true : index === itemIndexForAttribute))
        ? filteredOptions[index] || getOptions(attributes, currentAttributeKey)
        : getOptions(attributes, currentAttributeKey)
      : filteredOptions[index] || getOptions(attributes, currentAttributeKey);

    const selectedKeys = getSelectedKeys(items[index].value);
    const valueMappingEntry = attributeMappings?.[currentAttributeKey];
    // The search term/page live on the per-attribute entry, which every row using that
    // attribute shares. Only the row actually typing may narrow itself to the raw server
    // page - otherwise a search on one row would strip a sibling row's selected value from
    // its options and render it blank. Non-searching rows keep the pinned union so their
    // saved value always resolves to a description.
    const rowOwnsValueSearch = isFocused && searchText.length > 0;
    const valueOptionSource = valueMappingEntry?.search && rowOwnsValueSearch
      ? (valueMappingEntry.page ?? [])
      : valueMappingEntry?.mappings;
    const sortValueOptionsSelectedFirst = (opts: IComboBoxOption[] | undefined): IComboBoxOption[] | undefined => {
      if (!opts) return opts;
      const sel = opts.filter(o => selectedKeys.includes(String(o.key)));
      const unsel = opts.filter(o => !selectedKeys.includes(String(o.key)));
      return [...sel, ...unsel];
    };
    const attributeValueOptions = groupingEnabled
      ? (groups.length > 0 &&
        (groupIndex === undefined && groupIndexForAttributeValue === -1 ? true : groupIndex === groupIndexForAttributeValue) &&
        (childIndex === undefined && childIndexForAttributeValue === -1 ? true : childIndex === childIndexForAttributeValue) &&
        (index === undefined && itemIndexForAttributeValue === -1 ? true : index === itemIndexForAttributeValue))
        ? sortValueOptionsSelectedFirst(filteredValueOptions[index]) || getValueOptions(valueOptionSource, selectedKeys)
        : getValueOptions(valueOptionSource, selectedKeys)
      : sortValueOptionsSelectedFirst(filteredValueOptions[index]) || getValueOptions(valueOptionSource, selectedKeys);

    const isMulti = (op?: string) => op === 'IN' || op === 'NOT IN';
    const multi = isMulti(item.equalityOperator);
    const hasMultiple = multi && selectedKeys.length > 1;
    const menuOpen = isOpen;
    const userTyping = rowOwnsValueSearch;
    const readOnly = !isJobWriter || !isEditable;

    // Transient feedback for the row that is typing: only surfaced when a completed search
    // matched nothing. In-flight searches stay silent, and the server-side cap is deliberately
    // not surfaced as a persistent hint.
    const mappingEntry = valueMappingEntry;
    const activeValueSearch = rowOwnsValueSearch ? mappingEntry?.search : undefined;
    const valueSearchHint = (activeValueSearch && !mappingEntry?.isSearching && mappingEntry?.page?.length === 0)
      ? strings.HROnboarding.valueSearchNoResults.replace('{0}', activeValueSearch)
      : undefined;

    const handleRowArrowNavigation = (event: React.KeyboardEvent<HTMLElement>) => {
      if (event.altKey || event.ctrlKey || event.metaKey) return;
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;

      const target = event.target as HTMLElement | null;
      if (!target) return;
      const expanded = target.getAttribute('aria-expanded') === 'true';
      if (expanded) return;

      const row = target.closest('[role="row"]') ?? target.closest('.ms-DetailsRow');
      if (!row) return;

      const focusables = Array.from(
        row.querySelectorAll<HTMLElement>(
          'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        )
      ).filter((element) => {
        const isDisabled = element.hasAttribute('disabled') || element.getAttribute('aria-disabled') === 'true';
        const isVisible = element.offsetParent !== null;
        return !isDisabled && isVisible;
      });

      const activeElement = document.activeElement as HTMLElement | null;
      const currentIndex = focusables.findIndex((element) => element === activeElement);
      if (currentIndex === -1) return;

      const nextIndex = event.key === 'ArrowRight' ? currentIndex + 1 : currentIndex - 1;
      const nextElement = focusables[nextIndex];
      if (!nextElement) return;

      event.preventDefault();
      event.stopPropagation();
      nextElement.focus();
    };

    switch (column?.key) {
      case 'upDown':
        return (
          <div className={classNames.upDown}>
            <ActionButton
              data-testid="hr-up-button"
              iconProps={{ iconName: 'ChevronUp' }}
              title={strings.HROnboarding.up}
              disabled={!isJobWriter || !isEditable}
              onClick={() => onUpClick(index, items)}
              style={{ marginTop: '-15px', marginBottom: '-5px' }}
            />
            <ActionButton
              data-testid="hr-down-button"
              iconProps={{ iconName: 'ChevronDown' }}
              title={strings.HROnboarding.down}
              disabled={!isJobWriter || !isEditable}
              onClick={() => onDownClick(index, items)}
              style={{ marginTop: '-5px', marginBottom: '-15px' }}
            />
          </div>
        );
      case 'attribute':
        return (
          <ComboBox
            data-testid="hr-attribute-combobox"
            selectedKey={currentAttributeKey}
            options={attributeOptions}
            onInputValueChange={(text) => onAttributeChange(text, index, groupIndex, childIndex)}
            onChange={(event, option) =>
              handleAttributeChange(event, option, index, groupIndex, childIndex)
            }
            onRenderOption={onRenderAttributeComboBoxOptions}
            onRenderList={onRenderValueComboBoxList}
            allowFreeInput
            autoComplete="off"
            useComboBoxAsMenuWidth={true}
            dropdownMaxWidth={500}
            disabled={isAttributeDisabled || !isJobWriter || !isEditable}
            errorMessage={
              isAttributeDisabled
                ? strings.HROnboarding.attributeDisabledErrorMessage.replace('{email}', displayEmail)
                : undefined
            }
            styles={{
              errorMessage: classNames.errorMessageStyles,
            }}
            ariaLabel={strings.HROnboarding.attribute}
            iconButtonProps={{ 'aria-label': strings.HROnboarding.comboBoxCaretAriaLabel }}
          />
        );
      case 'equalityOperator':
        return (
          <ComboBox
            data-testid="hr-equality-operator-dropdown"
            selectedKey={item.equalityOperator ? item.equalityOperator.toUpperCase() : item.equalityOperator}
            options={getValidOperatorsForType(attribute?.type)}
            onChange={(event, option) =>
              handleEqualityOperatorChange(event, option, index, groupIndex, childIndex)
            }
            onKeyDown={(event) => handleRowArrowNavigation(event as unknown as React.KeyboardEvent<HTMLElement>)}
            onRenderOption={onRenderOperatorOptions}
            allowFreeInput={false}
            useComboBoxAsMenuWidth={true}
            dropdownMaxWidth={500}
            disabled={isAttributeDisabled || !isJobWriter || !isEditable}
            styles={{ root: classNames.equalityOperator }}
            ariaLabel={strings.HROnboarding.equalityOperator}
            iconButtonProps={{ 'aria-label': strings.HROnboarding.comboBoxCaretAriaLabel }}
          />
        );
      case 'value':
        if (item.equalityOperator && item.equalityOperator.toString().toUpperCase() === 'IS') {
          return (
            <ComboBox
              data-testid="hr-value-combobox"
              selectedKey={items[index].value.toUpperCase()}
              options={nullOptions}
              onChange={(event, option) => handleAttributeValueChange(item.attribute, event, items[index].value, option, index, item.equalityOperator, groupIndex, childIndex)}
              allowFreeInput
              autoComplete="off"
              dropdownMaxWidth={500}
              disabled={isAttributeDisabled || !isJobWriter || !isEditable}
              title={strings.HROnboarding.attributeValue}
              iconButtonProps={{ 'aria-label': strings.HROnboarding.comboBoxCaretAriaLabel }}
            />
          );
        }
        else {
          if(areAttributeMappingsLoading && attribute?.hasMapping && !attributeMappings[items[index].attribute]) {
            return <Spinner size={SpinnerSize.small} label={strings.HROnboarding.loadingText} />
          }
          else if (attributeMappings && mappingEntry && (mappingEntry.mappings.length > 0 || mappingEntry.hasMore || !!mappingEntry.search)) {
            return (
              <Stack>
              <VirtualizedComboBox
                data-testid="hr-value-virtualized-combobox"
                componentRef={valueCbRef}
                selectedKey={(item.equalityOperator === 'IN' || item.equalityOperator === 'NOT IN') ? getSelectedKeys(items[index].value) : items[index].value && items[index].value.startsWith("'") && items[index].value.endsWith("'") ? items[index].value.slice(1,-1) : items[index].value}
                text={
                  multi
                    ? (
                        readOnly
                          // read-only: never clear to '', always show summary when multiple
                          ? (hasMultiple ? strings.HROnboarding.multipleItemsSelected : undefined)
                          // editable: your existing behavior
                          : (hasMultiple && !(userTyping || isOpen)
                              ? strings.HROnboarding.multipleItemsSelected
                              : (isFocused ? searchText : (selectedKeys.length === 1 ? attributeValueOptions.find(o => String(o.key) === selectedKeys[0])?.text || selectedKeys[0] : undefined)))
                      )
                    : undefined
                }
                options={
                  (item.equalityOperator === 'IN' || item.equalityOperator === 'NOT IN') && (!isJobWriter || !isEditable)
                    ? (() => {
                      const selectedKeys = getSelectedKeys(items[index].value);
                      return attributeValueOptions.filter(option => selectedKeys.includes(String(option.key)));
                    })()
                  : attributeValueOptions
                }
                onInputValueChange={(t) => {
                  if (readOnly) return;
                  setSearchText(t ?? '');
                  onAttributeValueChange(t, index, currentAttributeKey, groupIndex, childIndex);
                }}
                onFocus={() => {
                  setIsFocused(true);
                  if (!readOnly && multi) setSearchText('');  // <-- don’t clear in read-only
                }}onBlur={() => {
                  setIsFocused(false);
                  if (!readOnly && multi) setSearchText('');  // <-- keep text intact in read-only
                }}
                onChange={(event, option) => {
                  if (readOnly) return;
                  handleAttributeValueChange(item.attribute, event, items[index].value, option, index, item.equalityOperator, groupIndex, childIndex);
                  if (isMulti(item.equalityOperator)) setSearchText('');
                  // Only multi-select keeps picking after a search-driven pick; a single-select
                  // operator (=, <>) is done once a value is chosen, so let the menu close.
                  setShouldReopen(multi && isOpen && userTyping);
                }}
                onRenderOption={onRenderValueComboBoxOptions}
                onRenderList={onRenderValueComboBoxList}
                allowFreeInput={!readOnly}
                multiSelect={(item.equalityOperator === 'IN' || item.equalityOperator === 'NOT IN') ? true : false}
                autoComplete="off"
                useComboBoxAsMenuWidth={false}
                dropdownMaxWidth={500}
                onMenuOpen={() => {
                  setIsOpen(true);
                  setIsFocused(true);
                  if (!readOnly && multi) setSearchText('');  // <-- don’t clear in read-only
                }}
                onMenuDismissed={() => {
                  const reopen = shouldReopen;
                  setIsOpen(false);
                  if (reopen) {
                    requestAnimationFrame(() => valueCbRef.current?.focus(true));
                    setShouldReopen(false);
                  } else {
                    setIsFocused(false);
                    if (!readOnly) {
                      setSearchText('');
                      onAttributeValueChange('', index, currentAttributeKey, groupIndex, childIndex);
                    }
                  }
                }}
                onKeyDown={(e) => {
                  if (!readOnly) return;
                  const k = e.key;
                  if (k.length === 1 || k === 'Backspace' || k === 'Delete') {
                    e.preventDefault();
                  }
                }}
                disabled={(() => {
                  const selectedKeys = getSelectedKeys(items[index].value);
                  return isAttributeDisabled ||
                    (item.equalityOperator !== 'IN' && item.equalityOperator !== 'NOT IN' || selectedKeys.length === 1) && (!isJobWriter || !isEditable)
                })()}
                title={strings.HROnboarding.attributeValue}
                iconButtonProps={{ 'aria-label': strings.HROnboarding.comboBoxCaretAriaLabel }}
                calloutProps={{styles: { calloutMain: { height: '300px', overflowY: 'auto' }}}}
                styles={
                  (item.equalityOperator === 'IN' || item.equalityOperator === 'NOT IN') && (!isJobWriter || !isEditable)
                    ? {
                        root: classNames.readOnlyComboBox,
                        input: classNames.readOnlyComboBoxInput
                      }
                    : undefined
                }
              />
              {!readOnly && valueSearchHint && (
                <Text data-testid="hr-value-search-hint" variant="tiny" block>{valueSearchHint}</Text>
              )}
              </Stack>
            );
          } else {
            return (
              <TextField
                data-testid="hr-value-textfield"
                value={
                  items[index].value && items[index].value.startsWith("'") && items[index].value.endsWith("'")
                    ? items[index].value.slice(1,-1)
                    : (item.equalityOperator === 'IN' || item.equalityOperator === 'NOT IN') && items[index].value && items[index].value.startsWith('(') && items[index].value.endsWith(')')
                      ? items[index].value.slice(1,-1)
                      : items[index].value
                }
                onChange={(event, newValue) => handleTAttributeValueChange(item.attribute, event, newValue!, index, item.equalityOperator, groupIndex, childIndex)}
                onBlur={(event) => handleBlur(item.attribute, event, index, item.equalityOperator, groupIndex, childIndex)}
                styles={{ fieldGroup: classNames.textField }}
                validateOnLoad={false}
                validateOnFocusOut={false}
                disabled={isAttributeDisabled || !isJobWriter || !isEditable}
                title={strings.HROnboarding.attributeValue}
              />
            );
          }
        }
      case 'andOr':
        const isLastItem = index >= items.length - 1;
        const selectedKey = item.andOr && item.andOr.trim() !== ''
          ? item.andOr.charAt(0).toUpperCase() + item.andOr.slice(1).toLowerCase()
          : (isLastItem ? null : undefined);

        return (
          <Dropdown
            data-testid="hr-andor-dropdown"
            selectedKey={selectedKey}
            onChange={(event, option) => handleOrAndOperatorChange(event, option, index, groupIndex, childIndex)}
            options={orAndOperatorOptions}
            styles={{ root: classNames.root, title: classNames.andOr }}
            disabled={isAttributeDisabled || !isJobWriter || !isEditable}
            title={strings.HROnboarding.orAndOperator}
          />
        );
      case 'remove':
        return (
          <ActionButton
            data-testid="hr-remove-button"
            className={`${classNames.removeButton} ${(!isJobWriter || !isEditable) ? classNames.removeButtonDisabled : ''}`}
            iconProps={{ iconName: "Blocked2" }}
            onClick={() => removeComponent(index ?? -1, groupIndex, childIndex)}
            disabled={!isJobWriter || !isEditable}>
            {strings.remove}
          </ActionButton>
        );
      default:
        return (
          <div className={classNames.root}>
            <Label />
          </div>
        );
    }
  }
  else {
    return (<div className={classNames.root} />);
  }
};
