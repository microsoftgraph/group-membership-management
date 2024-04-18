// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption, SpinButton, NormalPeoplePicker, DirectionalHint, IDropdownOption, ActionButton, DetailsList, DetailsListLayoutMode, Dropdown, Selection, IColumn, ComboBox, IComboBoxOption, IComboBox, IDragDropEvents, mergeStyles } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import type {
  HRQuerySourceProps,
  HRQuerySourceStyleProps,
  HRQuerySourceStyles,
} from './HRQuerySource.types';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { AppDispatch } from '../../store';
import { useDispatch, useSelector } from 'react-redux';
import { fetchOrgLeaderDetails } from '../../store/orgLeaderDetails.api';
import { getJobOwnerFilterSuggestions } from '../../store/jobs.api';
import { updateOrgLeaderDetails, selectOrgLeaderDetails, selectObjectIdEmployeeIdMapping } from '../../store/orgLeaderDetails.slice';
import { selectJobOwnerFilterSuggestions } from '../../store/jobs.slice';
import { manageMembershipIsEditingExistingJob } from '../../store/manageMembership.slice';
import { fetchDefaultSqlMembershipSourceAttributes } from '../../store/sqlMembershipSources.api';
import { selectAttributes, selectSource } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipAttribute } from '../../models';
import { IFilterPart } from '../../models/IFilterPart';

export const getClassNames = classNamesFunction<HRQuerySourceStyleProps, HRQuerySourceStyles>();

export const HRQuerySourceBase: React.FunctionComponent<HRQuerySourceProps> = (props: HRQuerySourceProps) => {
  const { className, styles, partId, onSourceChange } = props;
  const classNames: IProcessedStyleSet<HRQuerySourceStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const stackTokens: IStackTokens = {
    childrenGap: 30
  };

  type ChildType = {
    filter: string;
  };

  type FilteredOptionsState = {
    [key: number]: IComboBoxOption[];
  };

  const dispatch = useDispatch<AppDispatch>();
  const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
  const objectIdEmployeeIdMapping = useSelector(selectObjectIdEmployeeIdMapping);
  const ownerPickerSuggestions = useSelector(selectJobOwnerFilterSuggestions);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const [isDragAndDropEnabled, setIsDragAndDropEnabled] = useState(false);
  const [isDisabled, setIsDisabled] = useState(true);
  const [includeOrg, setIncludeOrg] = useState(false);
  const [includeFilter, setIncludeFilter] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const [children, setChildren] = useState<ChildType[]>([]);
  const excludeLeaderQuery = `EmployeeId <> ${source.manager?.id}`
  const attributes = useSelector(selectAttributes);
  const hrSource = useSelector(selectSource);
  const [filteredOptions, setFilteredOptions] = useState<FilteredOptionsState>({});
  const [items, setItems] = useState<IFilterPart[]>([]);
  let options: IComboBoxOption[] = [];
  const draggedItem = useRef<any | undefined>();
  const draggedIndex = useRef<number>(-1);

  useEffect(() => {
    if (children.length === 0) {
      setIncludeFilter(false);
      setFilteredOptions({});
    } else {
      let items: IFilterPart[] = children.map((child, index) => ({
        attribute: child.filter.split(' ')[0],
        equalityOperator: child.filter.split(' ')[1],
        value: child.filter.split(' ')[2],
        andOr: child.filter.split(' ')[3]
      }));
      setItems(items);
    }
  }, [children]);

  useEffect(() => {
    setFilteredOptions({});
  }, [children]);

  useEffect(() => {
    if (!includeOrg) { setErrorMessage(''); }
  }, [includeOrg]);


  useEffect(() => {
    if (children.length > 0) {
      let newStr = "";
      for (let i = 0; i < children.length; i++) {
        newStr += children[i].filter;
        if (i < children.length - 1) {
            newStr += " ";
        }
      }
      if (isDragAndDropEnabled) {
        setSource(prevSource => {
          const newSource = { ...prevSource, filter: newStr };
          onSourceChange(newSource, partId);
          return newSource;
        });
      }
    }
  }, [children]);


  const getOptions = (attributes?: SqlMembershipAttribute[]): IComboBoxOption[] => {
    options = attributes?.map((attribute, index) => ({
      key: attribute.name,
      text: attribute.name,
    })) || [];
    return options;
  };

  useEffect(() => {
    const regex = /( And | Or )/g;
    if (props.source.filter != undefined) {
      const parts = props.source.filter.split(regex);
      let childFilters = [];
      let currentFilter = "";
      for (let i = 0; i < parts.length; i += 2) {
        currentFilter = parts[i].trim();
        if (i + 1 < parts.length) {
          currentFilter += parts[i + 1];
        }
        childFilters.push(currentFilter);
    }
    setChildren(childFilters.map(filter => ({
      filter
    })));
    }
  }, [props.source.filter]);

  useEffect(() => {
    setIsDisabled(!orgLeaderDetails.maxDepth);
  }, [orgLeaderDetails.maxDepth]);

  useEffect(() => {
    if (orgLeaderDetails.employeeId > 0 && partId === orgLeaderDetails.partId) {
      const id: number = orgLeaderDetails.employeeId;
      const newSource = {
        ...props.source,
        manager: {
          ...props.source.manager,
          id: id
        }
      };
      setSource(newSource);
      onSourceChange(newSource, partId);
    }
  }, [orgLeaderDetails.employeeId, orgLeaderDetails.objectId]);

  useEffect(() => {
    if (orgLeaderDetails.employeeId === 0 && orgLeaderDetails.maxDepth === 0 && includeOrg && partId === orgLeaderDetails.partId) {
      setErrorMessage(hrSource?.name && hrSource?.name !== "" ?
      orgLeaderDetails.text + strings.HROnboarding.customOrgLeaderMissingErrorMessage + hrSource?.name + strings.HROnboarding.source :
      orgLeaderDetails.text + strings.HROnboarding.orgLeaderMissingErrorMessage);
    }
  }, [orgLeaderDetails]);

  const getPickerSuggestions = async (
    filterText: string
  ): Promise<IPersonaProps[]> => {
    return filterText && ownerPickerSuggestions ? ownerPickerSuggestions : [];
  };

  const handleOrgLeaderInputChange = (input: string): string => {
    setIncludeOrg(true);
    setErrorMessage('');
    dispatch(getJobOwnerFilterSuggestions({displayName: input, alias: input}))
    return input;
  }

  const handleOrgLeaderChange = (items?: IPersonaProps[] | undefined) => {
    setIncludeOrg(true);
    setIsDisabled(true);
    if (items !== undefined && items.length > 0) {
      dispatch(fetchOrgLeaderDetails({
        objectId: items[0].id as string,
        key: items[0].key as number,
        text: items[0].text as string,
        partId: partId as number
      }))
    }
  };

  const handleDepthChange = React.useCallback((event: React.SyntheticEvent<HTMLElement>, newValue?: string) => {
    const nonNumericRegex = /[^0-9]/g;
    if (newValue && nonNumericRegex.test(newValue)) {
      setErrorMessage(strings.HROnboarding.invalidInputErrorMessage);
      return;
    }
    const depth = newValue?.trim() !== '' ? Number(newValue) : undefined;
    setSource(prevSource => {
      const newSource = {
        ...prevSource,
        manager: {
          ...prevSource.manager,
          depth
        }
      };
      onSourceChange(newSource, partId);
      return newSource;
    });
  }, []);

  const handleFilterChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const filter = newValue;
    setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const addComponent = () => {
    setErrorMessage('');
    setSource(props.source);
    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    let result = true;
    if (segments) {
        for (let i = 0; i < segments.length; i++) {
            const parts = segments[i].trim().split(' ');
            if (parts.length < 4) {
                result = false;
                break;
            }
        }
    }
    if (result || children.length === 0) {
    setChildren(prevChildren => [...prevChildren, { filter: ''}]);
    } else {
      setErrorMessage(strings.HROnboarding.missingAttributeErrorMessage);
    }
  };

  const removeComponent = (indexToRemove: number) => {
    if (indexToRemove === -1) return;
    setFilteredOptions({});
    const childToRemove = children[indexToRemove];
    const newFilter = props.source.filter?.replace(childToRemove.filter, '').trim();
    setSource(prevSource => {
        const newSource = { ...prevSource, filter: newFilter };
        onSourceChange(newSource, partId);
        return newSource;
    });
    setChildren(prevChildren => prevChildren.filter((_, index) => index !== indexToRemove));
  };

  const yesNoOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no }
  ];

  const orAndOperatorOptions: IDropdownOption[] = [
    { key: '', text: '' },
    { key: 'Or', text: strings.or },
    { key: 'And', text: strings.and }
  ];

  const handleIncludeOrgChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    if (option?.key === "No") {
      setIncludeOrg(false);
      dispatch(updateOrgLeaderDetails({ employeeId: -1 }));
      const id = undefined;
      const depth = undefined;
      setSource(prevSource => {
        const newSource = {
          ...prevSource,
          manager: {
            ...prevSource.manager,
            id,
            depth
          }
        };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      setIncludeOrg(true);
    }
  }

  const handleIncludeFilterChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    if (option?.key === "No") {
      setIncludeFilter(false);
      setFilteredOptions({});
      const filter = "";
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      if (children.length === 0) {
        addComponent();
      }
      setIncludeFilter(true);
      dispatch(fetchDefaultSqlMembershipSourceAttributes());
      setSource(prevSource => {
        const newSource = { ...prevSource };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  }

  const handleIncludeLeaderChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    setErrorMessage('');
    let filter: string;
    if (option?.key === "No") {
      if (!source.manager?.id)
      {
        setErrorMessage(hrSource?.name && hrSource?.name !== "" ?
          orgLeaderDetails.text + strings.HROnboarding.customOrgLeaderMissingErrorMessage + hrSource?.name + strings.HROnboarding.source :
          orgLeaderDetails.text + strings.HROnboarding.orgLeaderMissingErrorMessage);
        return;
      }
      if (source.filter && source.filter !== "") {
        const endsWithAndOr = /( And| Or)$/i.test(source.filter);
        filter = endsWithAndOr ? `${source.filter} ${excludeLeaderQuery}` : `${source.filter} And ${excludeLeaderQuery}`;
      } else {
        filter = excludeLeaderQuery;
      }
    }
    else if (option?.key === "Yes") {
      if (props.source.filter?.includes(excludeLeaderQuery)) {
        const regex = new RegExp(`(And|Or) ${excludeLeaderQuery}|${excludeLeaderQuery} (And|Or)|${excludeLeaderQuery}`, 'g');
        filter = props.source.filter?.replace(regex, '').trim();
      }
    }
    setSource(prevSource => {
      const newSource = { ...prevSource, filter };
      onSourceChange(newSource, partId);
      return newSource;
    });
  }

  const equalityOperatorOptions: IDropdownOption[] = [
    { key: '=', text: '=' },
    { key: '<', text: '<' },
    { key: '<=', text: '<=' },
    { key: '>', text: '>'},
    { key: '>=', text: '>=' },
    { key: '<>', text: '<>' }
  ];

  const handleAttributeChange = (event: React.FormEvent<IComboBox>, item?: IComboBoxOption, index?: number): void => {
    if (item) {
      const updatedItems = items.map((it, idx) => {
        if (idx === index) {
          return { ...it, attribute: item.text };
        }
        return it;
      });
      setItems(updatedItems);
    }

    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      const a = item.text;
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + a;
      } else {
        filter = a;
      }
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && item) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');

      }
      if (words.length > 0) {
          words[0] = item.text;
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  };

  const handleEqualityOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number): void => {
    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      let a = item.text;
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + a;
      } else {
        filter = a;
      }
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && item) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
          words[1] = item.text;
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  };

  const handleAttributeValueChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '', index: number) => {
    const updatedItems = items.map((item, idx) => {
        if (idx === index) {
            return { ...item, value: newValue };
        }
        return item;
    });
    setItems(updatedItems);
};

  const handleBlur = (event: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>, index?: number) => {
    var newValue = event.target.value.trim();
    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (newValue !== "" && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + newValue;
      } else {
        filter = newValue;
      }
      setSource(prevSource => {
          const newSource = { ...prevSource, filter };
          onSourceChange(newSource, partId);
          return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && newValue) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
          words[2] = newValue;
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  }

  const handleOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number): void => {
    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      const a = item.text;
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + a;
      } else {
        filter = a;
      }
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && item) {
      let  words = segments[index].split(' ');
      if (words[0] === "") { words = segments[index].trim().split(' '); }
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
          words[3] = item.text;
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  };

  const onInputValueChange = (text: string, index: number) => {
    let newFilteredOptions = { ...filteredOptions };
    if (attributes && attributes.length > 0) {
      if (!text) {
        newFilteredOptions[index] = getOptions(attributes);
      } else {
        let options = getOptions(attributes);
        newFilteredOptions[index] = options.filter(opt => opt.text.toLowerCase().startsWith(text.toLowerCase()));
      }
      setFilteredOptions(newFilteredOptions);
    }
  };

  const columns = [
    {
      key: 'attribute',
      name: 'Attribute',
      fieldName: 'attribute',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false
    },
    {
      key: 'equalityOperator',
      name: 'Equality Operator',
      fieldName: 'equalityOperator',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false
    },
    {
      key: 'value',
      name: 'Value',
      fieldName: 'value',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false
    },
    {
      key: 'andOr',
      name: 'And/Or',
      fieldName: 'andOr',
      minWidth: 200,
      maxWidth: 200,
      isResizable: true
    },
    {
      key: 'remove',
      name: '',
      fieldName: 'remove',
      minWidth: 200,
      maxWidth: 200,
      isResizable: true
    }
  ];

  const onRenderItemColumn = (item?: any, index?: number, column?: IColumn): JSX.Element => {
    if (typeof index !== 'undefined' && items[index]) {
      switch (column?.key) {
        case 'attribute':
          return <ComboBox
          selectedKey={items[index].attribute}
          options={filteredOptions[index] || getOptions(attributes)}
          onInputValueChange={(text) => onInputValueChange(text, index)}
          onChange={(event, option) => handleAttributeChange(event, option, index)}
          allowFreeInput
          autoComplete="off"
          useComboBoxAsMenuWidth={true}
        />;
        case 'equalityOperator':
          return <Dropdown
          selectedKey={item.equalityOperator}
          onChange={(event, option) => handleEqualityOperatorChange(event, option, index)}
          options={equalityOperatorOptions}
          styles={{root: classNames.root, title: classNames.dropdownTitle}}
        />;
        case 'value':
          return <TextField
          value={items[index].value}
          onChange={(event, newValue) => handleAttributeValueChange(event, newValue!, index)}
          onBlur={(event) => handleBlur(event, index)}
          styles={{ fieldGroup: classNames.textField }}
          validateOnLoad={false}
          validateOnFocusOut={false}
        ></TextField>;
        case 'andOr':
          return <Dropdown
          selectedKey={item.andOr ? item.andOr : ""}
          onChange={(event, option) => handleOrAndOperatorChange(event, option, index)}
          options={orAndOperatorOptions}
          styles={{root: classNames.root, title: classNames.dropdownTitle}}
        />;
        case 'remove':
          return <ActionButton
          className={classNames.removeButton}
          iconProps={{ iconName: "Blocked2" }}
          onClick={() => removeComponent(index ?? -1)}>
          {strings.remove}
        </ActionButton>;
        default:
          return (
            <div>
              <Label />
            </div>
          );
      }
    }
    else {
      return (<div />);
    }
  };

  const getDragDropEvents = (): IDragDropEvents => {
    return {
      canDrop: () => true,
      canDrag: () => true,
      onDragEnter: () => "",
      onDragLeave: () => {},
      onDrop: (item) => {
        const selectedCount = selection.getSelectedCount();
        let sourceItems;
        if (selectedCount <= 0) {
          sourceItems = items;
        } else {
          sourceItems = selection.getSelection() as any[];
        }
        let transformedItems: ChildType[] = sourceItems.map((item) => ({
          filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
        }));

        const hasUndefined = transformedItems.some((item) => item.filter.includes("undefined"));
        if (hasUndefined) { return; }
        setIsDragAndDropEnabled(true);
        let newItems = [...items];
        let newSelectedIndices = [];
        if (selectedCount > 1) {
            const selectedItems = selection.getSelection() as any[];
            const insertIndex = newItems.indexOf(item);
            newItems = newItems.filter(i => !selectedItems.includes(i));
            newItems.splice(insertIndex, 0, ...selectedItems);          
            let allItems: ChildType[] = items.map((item) => ({
              filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
            }));

            const index = allItems.findIndex((item) => item.filter.includes("undefined"));
            if (insertIndex === index) {              
              return;
            }
            newSelectedIndices = selectedItems.map(item => newItems.indexOf(item));
            let newChildren: ChildType[] = newItems.map((item) => ({
              filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
            }));
            setChildren(newChildren);
            setItems(newItems);
        } else {
            const insertIndex = newItems.indexOf(item);
            newItems = newItems.filter(i => i !== draggedItem.current);
            newItems.splice(insertIndex, 0, draggedItem.current);
            newSelectedIndices.push(insertIndex);           
            let allItems: ChildType[] = items.map((item) => ({
              filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
            }));

            const index = allItems.findIndex((item) => item.filter.includes("undefined"));
            if (insertIndex === index) {            
              return;
            }
            let newChildren: ChildType[] = newItems.map((item) => ({
              filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
            }));
            setChildren(newChildren);
            setItems(newItems);
        }
        selection.setAllSelected(false);
        newSelectedIndices.forEach(index => selection.setIndexSelected(index, true, false));
      },
      onDragStart: (item?: any, itemIndex?: number) => {
        draggedItem.current = item;
        draggedIndex.current = itemIndex!;
      },
      onDragEnd: () => {
        draggedItem.current = undefined;
        draggedIndex.current = -1;
      },
    };
  };

  const [selection] = useState(() => new Selection({
    onSelectionChanged: () => {}
  }));

  return (
    <div className={classNames.root}>
      <Label>{strings.HROnboarding.includeOrg}</Label>
      <ChoiceGroup
        selectedKey={(includeOrg || source?.manager?.id) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeOrgChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={isEditingExistingJob}
      />

      {(includeOrg || source?.manager?.id) && (
      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
        <Stack.Item align="start">
          <div>
            <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.orgLeader}</Label>
              <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id="toolTipOrgLeaderId" calloutProps={{ gapSpace: 0 }}>
                <IconButton title={strings.HROnboarding.orgLeaderInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipOrgLeaderId" />
              </TooltipHost>
            </div>
            <NormalPeoplePicker
              aria-label={strings.HROnboarding.orgLeaderInfo}
              onResolveSuggestions={getPickerSuggestions}
              key={'normal'}
              resolveDelay={300}
              itemLimit={1}
              selectedItems={source?.manager?.id && !isDisabled ? [
                {
                  key: objectIdEmployeeIdMapping[source.manager.id]?.objectId.toString() || "",
                  text: objectIdEmployeeIdMapping[source.manager.id]?.text.toString() || ""
                },
              ] : undefined}
              onInputChange={handleOrgLeaderInputChange}
              onChange={handleOrgLeaderChange}
              styles={{ root: classNames.textField, text: classNames.textFieldGroup }}
              pickerCalloutProps={{directionalHint: DirectionalHint.bottomCenter}}
              disabled={isEditingExistingJob}
            />
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.depth}</Label>
              <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
              </TooltipHost>
            </div>
            <SpinButton
              value={source.manager?.depth?.toString()}
              disabled={isDisabled || isEditingExistingJob}
              min={0}
              max={(partId === orgLeaderDetails.partId) ? orgLeaderDetails.maxDepth : 100}
              step={1}
              onChange={handleDepthChange}
              incrementButtonAriaLabel={strings.HROnboarding.incrementButtonAriaLabel}
              decrementButtonAriaLabel={strings.HROnboarding.decrementButtonAriaLabel}
              styles={{ spinButtonWrapper: classNames.spinButton }}
            />
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
            <Label>{strings.HROnboarding.includeLeader}</Label>
            <ChoiceGroup
              selectedKey={(source.filter?.includes(excludeLeaderQuery))? strings.no : strings.yes}
              options={yesNoOptions}
              onChange={handleIncludeLeaderChange}
              styles={{
                root: classNames.horizontalChoiceGroup,
                flexContainer: classNames.horizontalChoiceGroupContainer
              }}
              disabled={isEditingExistingJob}
            />
          </div>
        </Stack.Item>
      </Stack>
      )}


      <div className={classNames.error}>
        {errorMessage}
      </div>
      <br />

      <Label>{strings.HROnboarding.includeFilter}</Label>
      <ChoiceGroup
        selectedKey={(includeFilter || source.filter) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeFilterChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={isEditingExistingJob}
      />

      {(includeFilter || source.filter) ?
        (
          <div>
          <DetailsList
            setKey="items"
            items={items}
            columns={columns}
            selectionPreservedOnEmptyClick={true}
            selection={selection}
            onRenderItemColumn={onRenderItemColumn}
            dragDropEvents={getDragDropEvents()}
            layoutMode={DetailsListLayoutMode.justified}
            ariaLabelForSelectionColumn="Toggle selection"
            ariaLabelForSelectAllCheckbox="Toggle selection for all items"
            checkButtonAriaLabel="select row"
            styles={{
              root: classNames.detailsList
            }}
          />
          <ActionButton iconProps={{ iconName: "CirclePlus" }} onClick={addComponent}>
            {strings.HROnboarding.addAttribute}
          </ActionButton>
          </div>
        ) : null
      }

    </div>
  );
};