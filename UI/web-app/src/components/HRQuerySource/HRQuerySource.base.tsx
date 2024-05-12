// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useRef, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption, SpinButton, NormalPeoplePicker, DirectionalHint, IDropdownOption, ActionButton, DetailsList, DetailsListLayoutMode, Dropdown, Selection, IColumn, ComboBox, IComboBoxOption, IComboBox, IDragDropEvents, SelectionMode, IDropdownStyles} from '@fluentui/react';
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
import { fetchOrgLeaderDetails, fetchOrgLeaderDetailsUsingId } from '../../store/orgLeaderDetails.api';
import { getJobOwnerFilterSuggestions } from '../../store/jobs.api';
import { updateOrgLeaderDetails, selectOrgLeaderDetails, selectObjectIdEmployeeIdMapping } from '../../store/orgLeaderDetails.slice';
import { selectJobOwnerFilterSuggestions } from '../../store/jobs.slice';
import { manageMembershipIsEditingExistingJob } from '../../store/manageMembership.slice';
import { fetchDefaultSqlMembershipSourceAttributes } from '../../store/sqlMembershipSources.api';
import { fetchAttributeValues } from '../../store/sqlMembershipSources.api';
import { selectAttributes, selectSource, selectAttributeValues, selectFilterGroups, setFilterGroups } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipAttribute, SqlMembershipAttributeValue } from '../../models';
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

  interface Group {
    name: string;
    items: IFilterPart[];
    children: Group[];
    andOr: string;
  }

  const dispatch = useDispatch<AppDispatch>();
  const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
  const objectIdEmployeeIdMapping = useSelector(selectObjectIdEmployeeIdMapping);
  const ownerPickerSuggestions = useSelector(selectJobOwnerFilterSuggestions);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const filterGroups = useSelector(selectFilterGroups);
  const [isDragAndDropEnabled, setIsDragAndDropEnabled] = useState(false);
  const [isDisabled, setIsDisabled] = useState(true);
  const [includeOrg, setIncludeOrg] = useState(false);
  const [includeFilter, setIncludeFilter] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const [children, setChildren] = useState<ChildType[]>([]);
  const excludeLeaderQuery = `EmployeeId <> ${source.manager?.id}`
  const attributes = useSelector(selectAttributes);
  const attributeValues = useSelector(selectAttributeValues);
  const hrSource = useSelector(selectSource);
  const [filteredOptions, setFilteredOptions] = useState<FilteredOptionsState>({});
  const [filteredValueOptions, setFilteredValueOptions] = useState<FilteredOptionsState>({});
  const [items, setItems] = useState<IFilterPart[]>([]);
  let options: IComboBoxOption[] = [];
  let valueOptions: IComboBoxOption[] = [];
  const draggedItem = useRef<any | undefined>();
  const draggedIndex = useRef<number>(-1);
  const [groups, setGroups] = useState<Group[]>([]);
  const [selectedIndices, setSelectedIndices] = useState<number[]>([]);
  const [groupingEnabled, setGroupingEnabled] = useState(false);
  const [filterTextEnabled, setFilterTextEnabled] = useState(false);
  const [groupQuery, setGroupQuery] = useState<string>();
  const [forceUpdate, setForceUpdate] = useState<number>(0);

  useEffect(() => {
    if (!groupingEnabled) {
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
    }
  }, [children]);

  useEffect(() => {
    if (!groupingEnabled) {
      const newGroups = [
        {
          name: "",
          items: items.map((item) => ({
            attribute: item.attribute,
            equalityOperator: item.equalityOperator,
            value: item.value,
            andOr: item.andOr,
          })),
          children: [],
          andOr: ""
        },
      ];
      setGroups(newGroups);
    }
  }, [items]);

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

  function stringifyGroup(group: Group): string {
    let result = '(';

    result += `${group.items.map((item, index) => {
      if (index < group.items.length - 1) {
          return item.attribute + ' ' + item.equalityOperator + ' ' + item.value + ' ' + item.andOr;
      } else {
          return item.attribute + ' ' + item.equalityOperator + ' ' + item.value;
      }
    }).join(' ')}`;

    if (group.children.length === 0) {
      result += ')';
    }

    if (group.andOr)
    {
      result += ` ${group.andOr} `;
    }

    group.children.forEach((child, index) => {
        result += stringifyGroup(child);
        if (index < group.children.length - 1) {
            result += ' ';
        }
    });

    if (group.children.length > 0) {
      result += ')';
    }

    return result;
  }

  function stringifyGroups(groups: Group[]): string {
      let result = '';

      groups.forEach((group, index) => {
          result += stringifyGroup(group);
          if (index < groups.length - 1) {
              result += ' ';
          }
      });

      return result;
  }

  function parseFilterPart(part: string): IFilterPart {
    const operators = ["<=", ">=", "<>", "=", ">", "<"];
    let operatorFound = '';
    let operatorIndex = -1;

    for (const operator of operators) {
      const index = part.indexOf(operator);
      if (index !== -1) {
        operatorFound = operator;
        operatorIndex = index;
        break;
      }
    }

    if (operatorIndex === -1) {
      setFilterTextEnabled(true);
    }
    const attribute = part.slice(0, operatorIndex).trim();
    const value = part.slice(operatorIndex + operatorFound.length).trim();

    return {
      attribute,
      equalityOperator: operatorFound,
      value,
      andOr: ""
    };
  }

function findPartsOfString(string: string, substringArray: { currentSegment: string, start: number; end: number }[]): { currentSegment: string, start: number; end: number, andOr: string }[] {
  const output: { currentSegment: string, start: number; end: number, andOr: "" }[] = [];
  let lastEnd = 0;

  for (const substringInfo of substringArray) {
      const { currentSegment, start, end } = substringInfo;

      // Add the segment between the end of the previous segment and the start of the current segment
      if (start > lastEnd) {
          output.push({
              currentSegment: string.substring(lastEnd, start),
              start: lastEnd,
              end: start - 1,
              andOr: ""
          });
      }

      // Add the current segment
      output.push({ currentSegment, start, end, andOr: "" });

      // Update lastEnd
      lastEnd = end + 1;
  }

  // Add the remaining part of the string after the last segment
  if (lastEnd < string.length) {
      output.push({
          currentSegment: string.substring(lastEnd),
          start: lastEnd,
          end: string.length - 1,
          andOr: ""
      });
  }

  return output;
}


  function parseGroup(input: string): Group[] {
    const groups: Group[] = [];
    const subStrings: string[] = [];
    let subStringsWithMoreDetails: { currentSegment: string, start: number; end: number}[] = [];
    let depth = 0;
    let currentSegment = '';
    // let start: number;
    // let end: number;
    let operators: string[] = [];

    input = input.trim();
    let start: number = 0;
    let end: number = input.length - 1;
    console.log("start", start);
    console.log("end", end);

    for (let i = 0; i < input.length; i++) {
        const char = input[i];

        if (char === '(') {
            if (depth > 0) {
                currentSegment += char;
            }
            depth++;
            if (depth === 1) start = i;
        } else if (char === ')') {
            depth--;
            if (depth === 0) {
                end = i;
                //groups.push(parseSegment(currentSegment));
                subStrings.push(currentSegment);
                // const index: number = input.indexOf(currentSegment);
                subStringsWithMoreDetails.push({ currentSegment, start, end});
                currentSegment = '';
            } else {
                currentSegment += char;
            }
        } else if (depth === 0 && (input.substr(i, 3) === ' Or' || input.substr(i, 4) === ' And')) {
            operators.push(input.substr(i, input.substr(i, 4) === ' And' ? 4 : 3).trim());
            i += operators[operators.length - 1].length - 1;
        } else if (depth > 0) {
            currentSegment += char;
        }
    }

    // for (let i = 0; i < groups.length - 1; i++) {
    //     groups[i].andOr = operators[i] || '';
    // }

    // console.log("subStrings", subStrings);
    // console.log("subStringsWithMoreDetails", subStringsWithMoreDetails);
    // console.log("NEW", input.substr(63, 140));
    var a = findPartsOfString(input, subStringsWithMoreDetails);
    // console.log("a", a);
    // console.log("operators", operators);


    a.forEach((segment, index) => {
      let modifiedSegment = segment.currentSegment.trim();
      console.log(`Modified Segment before: ${modifiedSegment}`);
      let startWord = '';
      let endWord = '';

      const lowerCaseSegment = modifiedSegment.toLowerCase();

      if (lowerCaseSegment.startsWith('and ')) {
          startWord = 'And';
          modifiedSegment = modifiedSegment.substring(4).trim();
      } else if (lowerCaseSegment.startsWith('or ')) {
          startWord = 'Or';
          modifiedSegment = modifiedSegment.substring(3).trim();
      }

      if (lowerCaseSegment.endsWith(' and')) {
          endWord = 'And';
          modifiedSegment = modifiedSegment.substring(0, modifiedSegment.length - 4).trim();
      } else if (lowerCaseSegment.endsWith(' or')) {
          endWord = 'Or';
          modifiedSegment = modifiedSegment.substring(0, modifiedSegment.length - 3).trim();
      }

      if (lowerCaseSegment === 'and') { // Additional condition for exact match
        startWord = 'And';
        modifiedSegment = '';
      } else if (lowerCaseSegment === 'or') { // Additional condition for exact match
        startWord = 'Or';
        modifiedSegment = '';
      }

      if (startWord !== '') {
          console.log(`Start word: ${startWord}`);
          a[index-1].andOr = startWord;
      }
      if (endWord !== '') {
          console.log(`End word: ${endWord}`);
          a[index].andOr = endWord;
      }

      console.log(`Modified Segment after: ${modifiedSegment}`);
      a[index].currentSegment = modifiedSegment;

      if (modifiedSegment === '') {
        a.splice(index, 1);
      } else {
          a[index].currentSegment = modifiedSegment;
      }

    });

    // console.log("A", a);

    a.forEach((currentSegment) => {
      groups.push(parseSegment(currentSegment.currentSegment));
    });

    for (let i = 0; i < groups.length - 1; i++) {
      groups[i].andOr = a[i].andOr || '';
    }

    console.log("groups", groups);

    return groups;
}

function parseSegment(segment: string, groupOperator?: string): Group {
    if (segment.includes('(') && segment.includes(')')) {
      let children: Group[] = [];
        const innerSegments = segment.match(/\((.*?)\)/g)?.map(innerSegment => innerSegment.replace(/^\(|\)$/g, ''));
        const contentOutsideParentheses = segment.replace(/\s*\([^)]*\)\s*/g, '||').split('||');
          if (innerSegments) {
            innerSegments.forEach((innerSegment, index) => {
              const childGroup = parseSegment(innerSegment, contentOutsideParentheses && contentOutsideParentheses.length >= 0 ? contentOutsideParentheses[index+1] : "");
              children.push(childGroup);
            });
          }

          let start = segment.indexOf('(');
          let end = segment.lastIndexOf(')');
          let remainingSegment = segment.substring(0, start) + segment.substring(end + 1);
          var match = remainingSegment.match(/\s*(Or|And)\s*$/i);
          var operator = match ? match[1] : null;
          remainingSegment = remainingSegment.replace(/\s*(Or|And)\s*/gi, '').trim();

          if (remainingSegment) {
            return {
                name: '',
                items: parseSegment(remainingSegment).items,
                children: children,
                andOr: operator ?? ''
            };
        }
    }
    const items = segment.split(/ And | Or /gi).map(parseFilterPart);
    const operators = segment.match(/(?: And | Or )/gi) || [];

    items.forEach((item, index) => {
        if (index < items.length - 1) {
            item.andOr = operators[index].trim();
        }
    });

    return {
        name: '',
        items,
        children: [],
        andOr: groupOperator ?? ''
    };
}

function setItemsBasedOnGroups(groups: Group[]) {
  let items: IFilterPart[] = [];
  groups.forEach(group => {
      items.push(...group.items);
      group.children.forEach(child => {
        items.push(...child.items);
      });
      setItemsBasedOnGroups(group.children);
  });
  setItems(items);
}

const getGroupLabels = (groups: Group[]) => {
  const str = stringifyGroups(groups);
  setGroupQuery(str);
  const groupQuery = str;
  const groupingEnabled = true;
  const filter = str;
  setSource(prevSource => {
      const newSource = { ...prevSource, filter };
      onSourceChange(newSource, partId);
      return newSource;
  });
  dispatch(setFilterGroups({partId, groupQuery, groupingEnabled}));
}

const checkType = (value: string, type: string | undefined): string => {
  switch (type) {
    case "nvarchar":
      if (value.startsWith("'") && value.endsWith("'")) {
        return value;
      } else {
          return `'${value}'`;
      }
    default:
      return value;
  }
};

  const getOptions = (attributes?: SqlMembershipAttribute[]): IComboBoxOption[] => {
    options = attributes?.map((attribute, index) => ({
      key: attribute.name,
      text: attribute.customLabel ? attribute.customLabel : attribute.name,
    })) || [];
    return options;
  };

  const getValueOptions = (attributeValues?: SqlMembershipAttributeValue[]): IComboBoxOption[] => {
    valueOptions = attributeValues?.map((attributeValue, index) => ({
      key: attributeValue.code,
      text: attributeValue.description ? attributeValue.description : attributeValue.code
    })) || [];
    return valueOptions;
  };

  useEffect(() => {
    if (props.source.filter && !groupingEnabled && (props.source.filter.includes("(") || props.source.filter.includes(")"))) {
      const a = parseGroup(props.source.filter);
      const b = setItemsBasedOnGroups(a);
      setGroups(a);
      setGroupingEnabled(true);
    }
    else {
      const regex = /( And | Or )/gi;
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
    if (source?.manager?.id) {
      if (objectIdEmployeeIdMapping[source.manager.id] === undefined) {        
        dispatch(fetchOrgLeaderDetailsUsingId({
          employeeId: source.manager.id,
          partId: partId as number
        }))
      }
    }
  }, [source]);

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
    setFilteredValueOptions({});
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
      setFilteredValueOptions({});
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

  interface UpdateParam {
    property: "attribute" | "value" | "andOr" | "equalityOperator";
    newValue: string;
  }

  const updateGroupItem = (updateParams: UpdateParam, index: number, otherIndex?: number): void => {
    const { property, newValue } = updateParams;
    const groupIndex = groups.findIndex(group =>
      group.children?.some(child =>
          child.items.some(item =>
              JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
          )
      ) || group.items?.some(item =>
          JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
      )
    );
    const childIndex = groupIndex !== -1 ? groups[groupIndex].children.findIndex(child =>
      child.items.some(item =>
          JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
      )
    ) : -1;

    const ifGroupItem = groups.some(group => isGroupItem(group, items[selectedIndices[0]]));
    const ifGroupChild = groups.some(group => isGroupChild(group, items[selectedIndices[0]]));

    if(!ifGroupItem && !ifGroupChild) {
      if (otherIndex != null) {
        groups[index].children[otherIndex].andOr = newValue;
      } else {
        groups[index].andOr = newValue;
      }
      setGroups(groups);
      getGroupLabels(groups);
      return;
    }

    if(ifGroupItem && groups[groupIndex].items[index ?? 0]) {
      groups[groupIndex].items[index ?? 0][property] = newValue;
    }
    if(ifGroupChild && groups[groupIndex].children[childIndex].items[index ?? 0]) {
      groups[groupIndex].children[childIndex].items[index ?? 0][property] = newValue;
    }

    const updatedItems = [...items];
    if (updatedItems[selectedIndices[0]]) updatedItems[selectedIndices[0]][property] = newValue;

    setItems(updatedItems);
    setGroups(groups);
    getGroupLabels(groups);
  }

  const handleAttributeChange = (event: React.FormEvent<IComboBox>, item?: IComboBoxOption, index?: number): void => {
    if (item) {
      const selectedAttribute = attributes?.find(attribute => attribute.name === item.key);
      dispatch(fetchAttributeValues({attribute: item.key as string, type: selectedAttribute?.type }));
      const updatedItems = items.map((it, idx) => {
        if (idx === index) {
          return { ...it, attribute: item.text };
        }
        return it;
      });
      setItems(updatedItems);
    }

    if (groupingEnabled && item && index != null) {
      const updateParams: UpdateParam = {
        property: "attribute",
        newValue: item.text
      };
      updateGroupItem(updateParams, index);
      return;
    }

    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      const a = item.key.toString();
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
          words[0] = item.key.toString();
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
    if (groupingEnabled && item && index != null) {
      const updateParams: UpdateParam = {
        property: "equalityOperator",
        newValue: item.text
      };
      updateGroupItem(updateParams, index);
      return;
    }
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

  const handleAttributeValueChange = (event: React.FormEvent<IComboBox>, item?: IComboBoxOption, index?: number): void => {
    if (item) {
      const updatedItems = items.map((it, idx) => {
        if (idx === index) {
          return { ...it, value: item.text };
        }
        return it;
      });

      const selectedValue = item.key.toString();
      let selectedValueAfterConversion: string = "";

      selectedValueAfterConversion = checkType(selectedValue, attributeValues[updatedItems[index ?? 0].attribute.toString()].type);
      setItems(updatedItems);

      if (groupingEnabled && index != null) {
        const updateParams: UpdateParam = {
          property: "value",
          newValue: selectedValueAfterConversion || selectedValue
        };
        updateGroupItem(updateParams, index);
        return;
      }

      const regex = /(?<= And | Or )/;
      let segments = props.source.filter?.split(regex);
      if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
        let filter: string;
        if (source.filter !== "") {
          filter = `${source.filter} ` + selectedValueAfterConversion || selectedValue;
        } else {
          filter = selectedValueAfterConversion || selectedValue;
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
            words[2] = selectedValueAfterConversion || selectedValue;
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
  };

  const handleTAttributeValueChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '', index: number) => {
    const updatedItems = items.map((it, idx) => {
        if (idx === index) {
            return { ...it, value: newValue };
        }
        return it;
    });

    const selectedValue = newValue;
    let selectedValueAfterConversion: string = "";

    if (attributeValues[items[index ?? 0].attribute]) {
      selectedValueAfterConversion = checkType(selectedValue, attributeValues[items[index ?? 0].attribute].type);
    }
    setItems(updatedItems);
  }

  const handleBlur = (event: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>, index?: number) => {
    if (groupingEnabled && index != null) {
      const updateParams: UpdateParam = {
        property: "value",
        newValue: event.target.value.trim()
      };
      updateGroupItem(updateParams, index);
      return;
    }
    var newValue = event.target.value.trim();
    const selectedValue = newValue;
    let selectedValueAfterConversion: string = "";
    if (attributeValues[items[index ?? 0].attribute]) {
      selectedValueAfterConversion = checkType(selectedValue, attributeValues[items[index ?? 0].attribute].type);
    }
    const regex = /(?<= And | Or )/;
    let segments = props.source.filter?.split(regex);
    if (selectedValueAfterConversion !== "" && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + selectedValueAfterConversion || selectedValue;
      } else {
        filter = selectedValueAfterConversion || selectedValue;
      }
      setSource(prevSource => {
          const newSource = { ...prevSource, filter };
          onSourceChange(newSource, partId);
          return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && selectedValueAfterConversion) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
          words[2] = selectedValueAfterConversion || selectedValue;
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

  const handleGroupOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number, childIndex?: number): void => {
    if (groupingEnabled && item && index != null) {
      const updateParams: UpdateParam = {
        property: "andOr",
        newValue: item.text
      };
      updateGroupItem(updateParams, index, childIndex);
      return;
    }
  }

  const handleOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number): void => {
    if (groupingEnabled && item && index != null) {
      const updateParams: UpdateParam = {
        property: "andOr",
        newValue: item.text
      };
      updateGroupItem(updateParams, index);
      return;
    }
    const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
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

  const onAttributeChange = (text: string, index: number) => {
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

  const onAttributeValueChange = (text: string, index: number) => {
    let newFilteredValueOptions = { ...filteredValueOptions };
    const currentAttributeValues = attributeValues[items[index].attribute].values || [];
    if (currentAttributeValues.length > 0) {
      if (!text) {
          newFilteredValueOptions[index] = getValueOptions(currentAttributeValues);
      } else {
          let valueOptions = getValueOptions(currentAttributeValues);
          newFilteredValueOptions[index] = valueOptions.filter(opt => opt.text.toLowerCase().startsWith(text.toLowerCase()));
      }
      setFilteredValueOptions(newFilteredValueOptions);
    }
  };

  const columns = [
     {
      key: 'upDown',
      name: '',
      fieldName: 'upDown',
      minWidth: 20,
      maxWidth: 20,
      isResizable: false
    },
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

  function onUpClick(index: number, items: IFilterPart[]) {
    let newItems = [...items];
    const insertIndex = index - 1;
    if (insertIndex < 0) { return; }

    let sourceItems: IFilterPart[] = [];
    sourceItems.push(items[index]);
    sourceItems.push(items[insertIndex]);
    let transformedItems: ChildType[] = sourceItems.map((item) => ({
      filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
    }));
    const hasUndefined = transformedItems.some((item) => item.filter.includes("undefined"));
    if (hasUndefined) { return; }

    setIsDragAndDropEnabled(true);
    newItems = newItems.filter((_, i) => i !== index);
    newItems.splice(insertIndex, 0, { ...items[index] });
    let newChildren: ChildType[] = newItems.map((item) => ({
      filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
    }));

    if (groupingEnabled) {
      const groupIndex = groups.findIndex(group =>
        group.children?.some(child =>
            child.items.some(item =>
                JSON.stringify(item) === JSON.stringify(items[index])
            )
        ) || group.items?.some(item =>
            JSON.stringify(item) === JSON.stringify(items[index])
        )
      );
      const childIndex = groupIndex !== -1 ? groups[groupIndex].children.findIndex(child =>
        child.items.some(item =>
            JSON.stringify(item) === JSON.stringify(items[index])
        )
      ) : -1;

      if (groupIndex !== -1 && childIndex === -1) {
        groups[groupIndex].items = newItems;
        setGroups(groups);
        getGroupLabels(groups);
        setItemsBasedOnGroups(groups);
      }
      else if (groupIndex !== -1 && childIndex !== -1) {
        groups[groupIndex].children[childIndex].items = newItems;
        setGroups(groups);
        getGroupLabels(groups);
        setItemsBasedOnGroups(groups);
      }
    }
    else {
      groups[0].items = newItems;
      setGroups(groups);
      setChildren(newChildren);
      setItems(newItems);
    }
  }

  function onDownClick(index: number, items: IFilterPart[]) {
    let newItems = [...items];
    const insertIndex = index + 1;

    if (insertIndex >= items.length) { return; }

    let sourceItems: IFilterPart[] = [];
    sourceItems.push(items[index]);
    sourceItems.push(items[insertIndex]);
    let transformedItems: ChildType[] = sourceItems.map((item) => ({
      filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
    }));
    const hasUndefined = transformedItems.some((item) => item.filter.includes("undefined"));
    if (hasUndefined) { return; }

    setIsDragAndDropEnabled(true);
    newItems = newItems.filter((_, i) => i !== index);
    newItems.splice(insertIndex, 0, { ...items[index] });
    let newChildren: ChildType[] = newItems.map((item) => ({
      filter: `${item.attribute} ${item.equalityOperator} ${item.value} ${item.andOr}`,
    }));

    if (groupingEnabled) {
      const groupIndex = groups.findIndex(group =>
        group.children?.some(child =>
            child.items.some(item =>
                JSON.stringify(item) === JSON.stringify(items[index])
            )
        ) || group.items?.some(item =>
            JSON.stringify(item) === JSON.stringify(items[index])
        )
      );
      const childIndex = groupIndex !== -1 ? groups[groupIndex].children.findIndex(child =>
        child.items.some(item =>
            JSON.stringify(item) === JSON.stringify(items[index])
        )
      ) : -1;

      if (groupIndex !== -1 && childIndex === -1) {
        groups[groupIndex].items = newItems;
        setGroups(groups);
        getGroupLabels(groups);
        setItemsBasedOnGroups(groups);
      }
      else if (groupIndex !== -1 && childIndex !== -1) {
        groups[groupIndex].children[childIndex].items = newItems;
        setGroups(groups);
        getGroupLabels(groups);
        setItemsBasedOnGroups(groups);
      }
    }
    else {
      groups[0].items = newItems;
      setGroups(groups);
      setChildren(newChildren);
      setItems(newItems);
    }
  }


  function onGroupUpClick(index: number) {
    let newGroups = [...groups];
    const insertIndex = index - 1;
    if (insertIndex < 0) { return; }

    setIsDragAndDropEnabled(true);
    newGroups = newGroups.filter((_, i) => i !== index);
    newGroups.splice(insertIndex, 0, { ...groups[index] });

    setGroups(newGroups);
    getGroupLabels(newGroups);
    setItemsBasedOnGroups(newGroups);
  }

  function onGroupDownClick(index: number) {
    let newGroups = [...groups];
    const insertIndex = index + 1;
    if (insertIndex > groups.length) { return; }

    setIsDragAndDropEnabled(true);
    newGroups = newGroups.filter((_, i) => i !== index);
    newGroups.splice(insertIndex, 0, { ...groups[index] });

    setGroups(newGroups);
    getGroupLabels(newGroups);
    setItemsBasedOnGroups(newGroups);
  }

  const onRenderItemColumn = (items: IFilterPart[], item?: any, index?: number, column?: IColumn): JSX.Element => {
    if (typeof index !== 'undefined' && items[index]) {
      switch (column?.key) {
        case 'upDown':
          return <div className={classNames.upDown}>
            <ActionButton iconProps={{ iconName: 'ChevronUp' }} onClick={() => onUpClick(index, items)} style={{ marginTop: '-15px', marginBottom: '-5px' }} />
            <ActionButton iconProps={{ iconName: 'ChevronDown' }} onClick={() => onDownClick(index, items)} style={{ marginTop: '-5px', marginBottom: '-15px' }} />
          </div>;
        case 'attribute':
          return <ComboBox
          selectedKey={item.attribute}
          options={filteredOptions[index] || getOptions(attributes)}
          onInputValueChange={(text) => onAttributeChange(text, index)}
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
          if (attributeValues && attributeValues[items[index].attribute] && attributeValues[items[index].attribute].values.length > 0) {
            return <ComboBox
              selectedKey={items[index].value}
              options={filteredValueOptions[index] || getValueOptions(attributeValues[items[index].attribute].values)}
              onInputValueChange={(text) => onAttributeValueChange(text, index)}
              onChange={(event, option) => handleAttributeValueChange(event, option, index)}
              allowFreeInput
              autoComplete="off"
              useComboBoxAsMenuWidth={true}
              />
          } else {
            return <TextField
              value={items[index].value}
              onChange={(event, newValue) => handleTAttributeValueChange(event, newValue!, index)}
              onBlur={(event) => handleBlur(event, index)}
              styles={{ fieldGroup: classNames.textField }}
              validateOnLoad={false}
              validateOnFocusOut={false}
          ></TextField>;
          }
        case 'andOr':
          return (
            !groupingEnabled ? (
              <Dropdown
                selectedKey={item.andOr ? item.andOr.charAt(0).toUpperCase() + item.andOr.slice(1).toLowerCase() : ""}
                onChange={(event, option) => handleOrAndOperatorChange(event, option, index)}
                options={orAndOperatorOptions}
                styles={{ root: classNames.root, title: classNames.dropdownTitle }}
              />
            ) : (
              index >= 0 && index < items.length - 1 ? (
                <Dropdown
                  selectedKey={item.andOr ? item.andOr.charAt(0).toUpperCase() + item.andOr.slice(1).toLowerCase() : ""}
                  onChange={(event, option) => handleOrAndOperatorChange(event, option, index)}
                  options={orAndOperatorOptions}
                  styles={{ root: classNames.root, title: classNames.dropdownTitle }}
                />
              ) : (<div />)
            )
          );
        case 'remove':
          return (
            !groupingEnabled ? (
            <ActionButton
            className={classNames.removeButton}
            iconProps={{ iconName: "Blocked2" }}
            onClick={() => removeComponent(index ?? -1)}>
            {strings.remove}
          </ActionButton>
          ) : (<div />)
        );
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

  const valueNeedsQuotes = (value: string, attribute: string) => {
    return attributeValues[attribute].values.length > 0 && attributeValues[attribute].type === "nvarchar" ? `'${value}'` : value;
  };

  const handleSelectionChanged = () => {
    setSelectedIndices(selection.getSelectedIndices());
  };

  const [selection] = useState(() => new Selection({
    onSelectionChanged: handleSelectionChanged
  }));

  function isGroupItem(group: Group, item: IFilterPart): boolean {
    return group.items.some(groupItem => JSON.stringify(groupItem) === JSON.stringify(item));
  }

  function isGroupChild(group: Group, item: IFilterPart): boolean {
    return group.children.some(childGroup => isGroupItem(childGroup, item));
  }

  function onUnGroupClick() {
    let newGroups = [...groups];
    let indices: { selectedItemIndex: number, groupIndex: number; childIndex: number }[] = [];
    const selectedItems = items.filter((item, index) => selectedIndices.includes(index));

    selectedItems.forEach((selectedItem, index) => {
      const ifGroupItem = groups.some(group => isGroupItem(group, selectedItem));
      const ifGroupChild = groups.some(group => isGroupChild(group, selectedItem));
      if (ifGroupItem)
      {
        const groupIndex = groups.findIndex(group => group.items?.some(item => JSON.stringify(item) === JSON.stringify(selectedItem)));
        if (groupIndex === 0) {
          return;
        }
        else if (groupIndex > 0) {
          indices.push({ selectedItemIndex: index, groupIndex, childIndex: -1 });
        }
      }
      if (ifGroupChild) {
        const groupIndex = groups.findIndex(group =>
          group.children?.some(child =>
              child.items.some(item =>
                  JSON.stringify(item) === JSON.stringify(selectedItem)
              )
          )
        );

        if (groupIndex !== -1) {
          const group = groups[groupIndex];
          const childIndex = group.children?.findIndex(child =>
            child.items?.some(item =>
                JSON.stringify(item) === JSON.stringify(selectedItem)
            )
          );
          indices.push({ selectedItemIndex: index, groupIndex, childIndex });
        }
      }
    });

    const groupIndices = indices.map(({ groupIndex }) => groupIndex);
    const allSameGroupIndex = groupIndices.every((groupIndex, index, array) => groupIndex === array[0]);
    const childIndices = indices.map(({ childIndex }) => childIndex);
    const allSameChildIndex = childIndices.every((childIndex, index, array) => childIndex === array[0]);

    let clonedNewGroups: Group[] = JSON.parse(JSON.stringify(newGroups));
    const childItems = selectedItems;
    const filterItems = (groupIndex: number) => {
      clonedNewGroups[groupIndex].items = clonedNewGroups[groupIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
        !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value && item.andOr === childItem.andOr));
      if (clonedNewGroups[groupIndex].items.length === 0) {
        clonedNewGroups[groupIndex].andOr = "";
        clonedNewGroups[groupIndex-1].andOr = "";
      }
    };
    const filterChildren = (groupIndex: number, childIndex: number) => {
      clonedNewGroups[groupIndex].children[childIndex].items = clonedNewGroups[groupIndex].children[childIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
        !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value && item.andOr === childItem.andOr));
      if (clonedNewGroups[groupIndex].children[childIndex].items.length === 0) {
        clonedNewGroups[groupIndex].children[childIndex].andOr = "";
      }
    };

    if (allSameGroupIndex && allSameChildIndex && groupIndices[0] > 0 && childIndices[0] >= 0) {
      clonedNewGroups[groupIndices[0]].items = [...clonedNewGroups[groupIndices[0]].items, ...childItems];
      filterChildren(groupIndices[0], childIndices[0]);
    }

    else if (allSameGroupIndex && groupIndices[0] > 0 && childIndices[0] === -1) {
      clonedNewGroups[0].items = [...clonedNewGroups[0].items, ...childItems];
      filterItems(groupIndices[0]);
    }

    clonedNewGroups = clonedNewGroups.filter((group: { items: any[]; children: any[]; }) =>
      group.items.length > 0 || group.children.some((child: { items: any[]; }) => child.items.length > 0)
    );

    clonedNewGroups.forEach((group: { children: any[]; andOr: string}) => {
      group.children = group.children.filter((child: { items: any[]; }) => child.items.length > 0);

    });
    setGroups(clonedNewGroups);
    getGroupLabels(clonedNewGroups);
    setSelectedIndices([]);
    selection.setAllSelected(false);
    setGroupingEnabled(true);
  }

    function onGroupClick() {
      let newGroups = [...groups];
      let indices: { selectedItemIndex: number, groupIndex: number; childIndex: number }[] = [];
      const selectedItems = items.filter((item, index) => selectedIndices.includes(index));
      selectedItems.forEach((selectedItem, index) => {

        const ifGroupItem = groups.some(group => isGroupItem(group, selectedItem));
        if (ifGroupItem)
        {
          const groupIndex = groups.findIndex(group => group.items?.some(item => JSON.stringify(item) === JSON.stringify(selectedItem)));
          if (groupIndex === 0) {
            indices.push({ selectedItemIndex: index, groupIndex, childIndex: -1 });
          }
          else if (groupIndex > 0) {
            indices.push({ selectedItemIndex: index, groupIndex, childIndex: -1 });
          }
        }
      });

      selectedItems.forEach((selectedItem, index) => {
        const ifGroupChild = groups.some(group => isGroupChild(group, selectedItem));
        if (ifGroupChild) {
          return;
        }
      });

      const groupIndices = indices.map(({ groupIndex }) => groupIndex);
      const allSameGroupIndex = groupIndices.every((groupIndex, index, array) => groupIndex === array[0]);

      let clonedNewGroups: Group[] = JSON.parse(JSON.stringify(newGroups));
      const childItems = selectedItems.map(selectedItem => ({ attribute: selectedItem.attribute, equalityOperator: selectedItem.equalityOperator, value: selectedItem.value, andOr: selectedItem.andOr }));
      const filterItems = (groupIndex: number) => {
        clonedNewGroups[groupIndex].items = clonedNewGroups[groupIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
          !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value && item.andOr === childItem.andOr));
      };

      if (allSameGroupIndex && groupIndices[0] === 0) {
        const newGroup: Group = {
          name: "",
          items: childItems,
          children: [],
          andOr: ""
        };
        clonedNewGroups.push(newGroup);
        clonedNewGroups[clonedNewGroups.length-2].andOr = selectedItems[0].andOr; // between groups
        filterItems(groupIndices[0]);
      }

      else if (allSameGroupIndex && groupIndices[0] > 0) {
        clonedNewGroups[groupIndices[0]].children = [
          ...(clonedNewGroups[groupIndices[0]].children || []),
          {
            name: ``,
            items: childItems,
            children: [],
            andOr: ""
          }
        ];
        if (clonedNewGroups[groupIndices[0]].children.length === 1)
        {
          clonedNewGroups[groupIndices[0]].andOr = selectedItems[0].andOr; // between group & children
        }
        else if (clonedNewGroups[groupIndices[0]].children.length > 1)
        {
          clonedNewGroups[groupIndices[0]].children[clonedNewGroups[groupIndices[0]].children.length-2].andOr = selectedItems[0].andOr; // between children
        }
        filterItems(groupIndices[0]);
      }
      clonedNewGroups = clonedNewGroups.filter((group: { items: string | any[]; children: string | any[]; }) => group.items?.length > 0 || group.children?.length > 0);
      setGroups(clonedNewGroups);
      getGroupLabels(clonedNewGroups);
      setSelectedIndices([]);
      selection.setAllSelected(false);
      setGroupingEnabled(true);
    }

  const dropdownStyles: Partial<IDropdownStyles> = {
    dropdown: { width: 100 },
  };

  const renderItems = (items: IFilterPart[], conjunction: string, index: number, isLastItem: boolean, isUpDownEnabled: boolean) => {
    const selection: Selection = new Selection({
      onSelectionChanged: () => handleSelectionChange(selection, index)
    });
    return (
      <div>
      {isUpDownEnabled && groups.length > 1 && (<div className={classNames.upDown}>
        <ActionButton iconProps={{ iconName: 'ChevronUp' }} onClick={() => onGroupUpClick(index)} style={{ marginTop: '15px', marginBottom: '-15px'}} />
        <ActionButton iconProps={{ iconName: 'ChevronDown' }} onClick={() => onGroupDownClick(index)} style={{ marginBottom: '-15px'}} />
      </div>)}
      <DetailsList
        styles={{ root: groups.length > 1 && items.length > 1 ? classNames.detailsListWithBorder : classNames.detailsList }}
        items={items}
        columns={columns}
        onRenderItemColumn={(item, index, column) => onRenderItemColumn(items, item, index, column)}
        selection={selection}
        selectionPreservedOnEmptyClick={true}
        layoutMode={DetailsListLayoutMode.justified}
      />
      </div>
    );
  };

  const renderGroup = (group: Group, parentIndex: number) => {
    return (
      <div>
      <Stack key={parentIndex}>
        <Stack tokens={{ childrenGap: 10 }}>
          {group.items.length > 0 && renderItems(group.items, 'And', parentIndex, parentIndex === groups.length - 1, true)}
          {((group.items && group.items.length > 0 && parentIndex !== groups.length - 1) || (group.children && group.children.length > 0)) && (
          <div>
          <Dropdown
            onChange={(event, option) => handleGroupOrAndOperatorChange(event, option, parentIndex)}
            selectedKey={group.andOr.charAt(0).toUpperCase() + group.andOr.slice(1).toLowerCase()}
            options={orAndOperatorOptions}
            styles={dropdownStyles}
          />
          </div>
          )}
          {group.children && renderChildren(group.children, parentIndex)}
        </Stack>
      </Stack>
      </div>
    );
  };

  const renderChildren = (children: Group[], parentIndex: number) => {
    return children.map((childGroup: Group, childIndex: number) => (
      <Stack key={parentIndex + '-' + childIndex} tokens={{ childrenGap: 10 }} style={{ paddingLeft: '50px' }}>
        <Label>{childGroup.name}</Label>
        <Stack tokens={{ childrenGap: 10 }}>
          {childGroup.items.length > 0 && renderItems(childGroup.items, childGroup.items[childGroup.items.length-1].andOr, parentIndex, childIndex === children.length - 1, false)}
          {((childGroup.items && childGroup.items.length > 0 && (childIndex !== children.length - 1 || parentIndex !== groups.length - 1)) || (childGroup.children && childGroup.children.length > 0)) && (
          <div>
          <Dropdown
            onChange={(event, option) => handleGroupOrAndOperatorChange(event, option, parentIndex, childIndex)}
            selectedKey={childGroup.andOr.charAt(0).toUpperCase() + childGroup.andOr.slice(1).toLowerCase()}
            options={orAndOperatorOptions}
            styles={dropdownStyles}
          />
          </div>
          )}
          {childGroup.children && renderChildren(childGroup.children, parentIndex)}
        </Stack>
      </Stack>
    ));
  };

  function handleSelectionChange(selection: Selection, index: number) {
    const selectedItems = selection.getSelection() as any[];
    const selectedIndices = selectedItems.map(selectedItem => {
      return items.findIndex(item =>
        item.attribute === selectedItem.attribute &&
        item.equalityOperator === selectedItem.equalityOperator &&
        item.value === selectedItem.value &&
        item.andOr === selectedItem.andOr);
    });
    setSelectedIndices(selectedIndices);
  }

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

      {(includeOrg || (source?.manager?.id && objectIdEmployeeIdMapping[source.manager.id] && objectIdEmployeeIdMapping[source.manager.id].text !== undefined)) && (
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
              selectedItems={source?.manager?.id && objectIdEmployeeIdMapping[source.manager.id] && !isDisabled ? [
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

      {(source.filter && filterTextEnabled) ?
       (
        <><div className={classNames.labelContainer}>
        <Label>{strings.HROnboarding.filter}</Label>
        <TooltipHost content={strings.HROnboarding.filterInfo} id="toolTipFilterId" calloutProps={{ gapSpace: 0 }}>
          <IconButton title={strings.HROnboarding.filterInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipFilterId" />
        </TooltipHost>
        </div>
        <TextField
          placeholder={strings.HROnboarding.filterPlaceHolder}
          multiline rows={3}
          resizable={true}
          value={source.filter?.toString()}
          onChange={handleFilterChange}
          styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
          validateOnLoad={false}
          validateOnFocusOut={false}
          disabled={isEditingExistingJob}
        ></TextField></>
        ) : attributes && attributes.length > 0 && (includeFilter || source.filter) ?
        (
          <div>
            <ActionButton
              iconProps={{ iconName: 'GroupObject' }}
              onClick={onGroupClick}
              disabled={!(selectedIndices.length > 1)}>
              Group
            </ActionButton>
            <ActionButton
              iconProps={{ iconName: 'GroupObject' }}
              onClick={onUnGroupClick}
              disabled={!(selectedIndices.length > 0 && groups.length > 0 && (groupingEnabled || (filterGroups[partId] && filterGroups[partId].groupingEnabled)))}>
              Ungroup
            </ActionButton>
          <br/>


          {(groupingEnabled || (filterGroups[partId] && filterGroups[partId].groupingEnabled) || groups != null) ? (
            <div>
              {groups.map((group: Group, index: number) => (
                <div>
                <React.Fragment key={index}>
                  {renderGroup(group, index)}
                </React.Fragment>
                </div>
              ))}
            </div>
            ) : (
            <DetailsList
              setKey="items"
              items={items}
              columns={columns}
              selectionPreservedOnEmptyClick={true}
              selection={selection}
              onRenderItemColumn={(item, index, column) => onRenderItemColumn(items, item, index, column)}
              layoutMode={DetailsListLayoutMode.justified}
              ariaLabelForSelectionColumn="Toggle selection"
              ariaLabelForSelectAllCheckbox="Toggle selection for all items"
              checkButtonAriaLabel="select row"
              styles={{
                root: classNames.detailsList
              }}
            />
          )}

          {!groupingEnabled && <ActionButton iconProps={{ iconName: "CirclePlus" }} onClick={addComponent}>
            {strings.HROnboarding.addAttribute}
          </ActionButton>}
          </div>
        ) : null
      }

      <div className={classNames.error}>
        {errorMessage}
      </div>
    </div>
  );
};