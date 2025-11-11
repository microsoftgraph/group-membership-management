// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, Text, ChoiceGroup, IChoiceGroupOption, IDropdownOption, ActionButton, DetailsList, DetailsListLayoutMode, Dropdown, Selection, IColumn, ComboBox, IComboBoxOption, IComboBox, Separator, ISelectableOption, ISelectableDroppableTextProps, format, IDetailsHeaderProps, DetailsHeader, IRenderFunction, IDetailsColumnRenderTooltipProps, VirtualizedComboBox, Spinner, SpinnerSize, PrimaryButton } from '@fluentui/react';
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
import { getSupportEmailAddress } from '../../store/settings.api';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { updateOrgLeaderDetails, selectOrgLeaderDetails, selectObjectIdEmployeeIdMapping } from '../../store/orgLeaderDetails.slice';
import { selectPeoplePickerSuggestions, updateJobOwnerFilterSuggestions } from '../../store/jobs.slice';
import { fetchDefaultSqlMembershipSourceAttributes } from '../../store/sqlMembershipSources.api';
import { fetchAttributeMappings } from '../../store/sqlMembershipSources.api';
import { selectAttributes, selectSource, selectAttributeMappings, setAttributeMappings, selectAreAttributeMappingsLoading } from '../../store/sqlMembershipSources.slice';
import { selectIsJobWriter } from '../../store/roles.slice';
import { SqlMembershipAttribute, SqlMembershipAttributeMapping } from '../../models';
import { IFilterPart } from '../../models/IFilterPart';
import { Group } from '../../models/Group';
import { containsSqlExpression, countOccurrences, parseGroup, stringifyGroups } from './QuerySerializer';
import { updateHRTitleWithNewLeader, updateHRTitleWithNewDepth } from '../../utils/titleGenerator';
import { equalityOperatorOptions, nullOptions, orAndOperatorOptions, yesNoOptions } from '../../models/Options';
import { selectSupportEmail, selectSupportEmailLoading, selectSupportEmailError, selectIsAITitleEnabled } from '../../store/settings.slice';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';
import { InfoWord } from '../InfoWord';
import { OrgLeader } from '../OrgLeader';
import { jsxFormat } from '../../utils/stringUtils';
import { selectIsGeneratingTitle } from '../../store/title.slice';
import { getTitle } from '../../store/title.api';
import { setIsMissingAndOrOperator } from '../../store/manageMembership.slice';
import { HRQueryItemColumn } from './components';

const PLACEHOLDER_OPERATOR = 'placeholder';

export const getClassNames = classNamesFunction<HRQuerySourceStyleProps, HRQuerySourceStyles>();

export const HRQuerySourceBase: React.FunctionComponent<HRQuerySourceProps> = (props: HRQuerySourceProps) => {

  const { className, styles, partId, onSourceChange, onEnableEdit, isEditable } = props;
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
  const isAITitleEnabled = useSelector(selectIsAITitleEnabled);
  const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
  const objectIdEmployeeIdMapping = useSelector(selectObjectIdEmployeeIdMapping);
  const ownerPickerSuggestions = useSelector(selectPeoplePickerSuggestions);
  const isJobWriter = useSelector(selectIsJobWriter);
  const [isDragAndDropEnabled, setIsDragAndDropEnabled] = useState(false);
  const [isDisabled, setIsDisabled] = useState(false);
  const [includeOrg, setIncludeOrg] = useState(false);
  const [includeFilter, setIncludeFilter] = useState(false);
  const [orgErrorMessage, setOrgErrorMessage] = useState<string>('');
  const [filterErrorMessage, setFilterErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const [children, setChildren] = useState<ChildType[]>([]);
  const attributes = useSelector(selectAttributes);
  const attributeMappings = useSelector(selectAttributeMappings);
  const isGeneratingTitle = useSelector(selectIsGeneratingTitle);
  const areAttributeMappingsLoading = useSelector(selectAreAttributeMappingsLoading);
  const hrSource = useSelector(selectSource);
  const [childIndexForAttribute, setChildIndexForAttribute] = useState<number>(-1);
  const [groupIndexForAttribute, setGroupIndexForAttribute] = useState<number>(-1);
  const [itemIndexForAttribute, setItemIndexForAttribute] = useState<number>(-1);
  const [filteredOptions, setFilteredOptions] = useState<FilteredOptionsState>({});
  const [filteredValueOptions, setFilteredValueOptions] = useState<FilteredOptionsState>({});
  const [items, setItems] = useState<IFilterPart[]>([]);
  const [childIndexForAttributeValue, setChildIndexForAttributeValue] = useState<number>(-1);
  const [groupIndexForAttributeValue, setGroupIndexForAttributeValue] = useState<number>(-1);
  const [itemIndexForAttributeValue, setItemIndexForAttributeValue] = useState<number>(-1);
  const [groups, setGroups] = useState<Group[]>([]);
  const [selectedItems, setSelectedItems] = useState<IFilterPart[]>([]);
  const [selectedIndices, setSelectedIndices] = useState<number[]>([]);
  const [groupingEnabled, setGroupingEnabled] = useState(false);
  const [filterTextEnabled, setFilterTextEnabled] = useState(false);
  const [expanded, setExpanded] = useState(true);
  const [orgLeaderUpdated, setOrgLeaderUpdated] = useState(false);
  const [selectedKeys, setSelectedKeys] = React.useState<string[]>([]);
  const [localTitle, setLocalTitle] = useState<string>("");
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);
  const email = useSelector(selectSupportEmail);
  const emailLoading = useSelector(selectSupportEmailLoading);
  const emailError = useSelector(selectSupportEmailError);
  const displayEmail = (!emailLoading && !emailError && email && email.trim() !== '') ? email : strings.HROnboarding.supportPlaceHolder;

  interface IExtendedComboBoxOption extends IComboBoxOption {
    description?: string;
  }

  useEffect(() => {
    dispatch(getSupportEmailAddress());
  }, [dispatch]);

  useEffect(() => {
    if (!groupingEnabled) {
      if (children.length === 0) {
        setIncludeFilter(false);
        setFilteredOptions({});
        setFilteredValueOptions({});
      } else {
        let items: IFilterPart[] = children.map((child, index) => {
          const parts = child.filter.trim().split(' ').filter(part => part !== '');

          // Handle two-word operators like "NOT IN"
          let attribute, equalityOperator;
          if (parts.length > 2 && parts[1] === "NOT" && parts[2] === "IN") {
            attribute = parts[0];
            equalityOperator = "NOT IN";
          } else {
            attribute = parts[0];
            equalityOperator = parts[1];
          }

          const result = findValueAndOr(parts);
          const filterPart: IFilterPart = {
            attribute,
            equalityOperator,
            value: result.value,
            andOr: result.andOr
          };
          return filterPart;
        });
        setItems(items);
      }
    }
  }, [children, attributes]);

  useEffect(() => {
    let mappedItems = items.map((item) => ({ attribute: item.attribute, equalityOperator: item.equalityOperator, value: item.value, andOr: item.andOr }));
    let att = mappedItems.map(item => item.attribute);
    let distinctAttributes = [...new Set(att)];
    for (let i = 0; i < distinctAttributes.length; i++) {
      if (attributeMappings && attributeMappings[distinctAttributes[i]] === undefined) {
        const selectedAttribute = attributes?.find(({ hasMapping, name }) => ((hasMapping && `${name}_Code` === distinctAttributes[i]) || (!hasMapping && name === distinctAttributes[i])));
        dispatch(fetchAttributeMappings({attribute: distinctAttributes[i] as string, type: selectedAttribute?.type, hasMapping: selectedAttribute?.hasMapping }));
      }
    }
    if (!groupingEnabled) {
      const newGroups = [{ name: "", items: mappedItems, children: [], andOr: "" }];
      setGroups(newGroups);
    }
  }, [items]);

  useEffect(() => {
    if (!includeOrg) { setOrgErrorMessage(''); }
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
          onSourceChange(newSource, partId, props.title);
          return newSource;
        });
      }
    }
  }, [children]);
  function findValueAndOr(words: string[]): { andOr: string, value: string } {
    let value = '';
    let andOr = '';
    // Check if it's a two-word operator like "NOT IN"
    let startIndex = 2;
    if (words.length > 2 && words[1] === "NOT" && words[2] === "IN") {
      startIndex = 3;
    }

    let andOrStartIndex = -1;
    for (let i = startIndex; i < words.length; i++) {
      const part = words[i].toLowerCase();
      if (part === 'and' || part === 'or' || part === PLACEHOLDER_OPERATOR) {
        andOrStartIndex = i;
        andOr = words.slice(i).join(' ');
        break;
      }
    }

    if (andOrStartIndex > -1) {
      // AND/OR found, value is everything from startIndex to andOrStartIndex
      value = words.slice(startIndex, andOrStartIndex).join(' ');
    } else {
      // No AND/OR found, value is everything from startIndex to end
      value = words.slice(startIndex).join(' ');
    }

    return { andOr, value };
  }

  /**
   * Helper function to handle common filter word manipulation logic
   * Handles operator changes and value updates while maintaining consistent spacing
   */
  function updateFilterWords(words: string[], newOperator?: string, newValue?: string, newAndOr?: string): string[] {
    const result = findValueAndOr(words);
    const prevOperator = words[1];
    const isPrevNotIn = prevOperator === "NOT" && words.length > 2 && words[2] === "IN";
    const isNewNotIn = newOperator === "NOT IN";

    if (newOperator) {
      // Handle operator changes
      if (isNewNotIn) {
        // Check if we're already "NOT IN" - if so, no change needed
        if (isPrevNotIn) {
          return words;
        }
        // Replace single-word operator with two-word operator
        words.splice(1); // Remove everything after attribute
        words.push("NOT", "IN");
        if (result.value) {
          // Wrap value in parentheses if not already wrapped
          let valueToAdd = result.value;
          if (!(valueToAdd.startsWith('(') && valueToAdd.endsWith(')'))) {
            valueToAdd = `(${valueToAdd})`;
          }
          words.push(valueToAdd);
        }
        if (result.andOr !== '') {
          words.push(result.andOr);
        }
      } else {
        // Handle single-word operators
        if (isPrevNotIn) {
          // Previous was "NOT IN", now changing to single-word operator
          words.splice(1); // Remove everything after attribute
          words.push(newOperator);
          if (result.value) {
            if (newOperator === "IN") {
              // Special case: changing from "NOT IN" to "IN" - preserve the full value list
              words.push(result.value);
            } else {
              // For non-IN operators, remove parentheses from values if they exist
              let cleanValue = result.value;
              if (cleanValue.startsWith('(') && cleanValue.endsWith(')')) {
                cleanValue = cleanValue.slice(1, -1);
                // If it was a multi-value IN clause, take just the first value
                const firstValue = cleanValue.split(',')[0].trim();
                cleanValue = firstValue;
              }
              words.push(cleanValue);
            }
          }
          if (result.andOr !== '') {
            words.push(result.andOr);
          }
        } else {
          // Normal single-word to single-word operator change
          words[1] = newOperator;
          if (prevOperator === "IN" && newOperator !== "IN") {
            // Switching FROM "IN" to a non-IN operator
            words.splice(2);
            if (result.value) {
              // For non-IN operators, remove parentheses from values if they exist
              let cleanValue = result.value;
              if (cleanValue.startsWith('(') && cleanValue.endsWith(')')) {
                cleanValue = cleanValue.slice(1, -1);
                // If it was a multi-value IN clause, take just the first value
                const firstValue = cleanValue.split(',')[0].trim();
                cleanValue = firstValue;
              }
              words.push(cleanValue);
            }
            if (result.andOr !== '') {
              words.push(result.andOr);
            }
          } else if (newOperator === "IN" && prevOperator !== "IN") {
            // Switching TO "IN" from a non-IN operator
            words.splice(2);
            if (result.value) {
              // Wrap value in parentheses if not already wrapped
              let valueToAdd = result.value;
              if (!(valueToAdd.startsWith('(') && valueToAdd.endsWith(')'))) {
                valueToAdd = `(${valueToAdd})`;
              }
              words.push(valueToAdd);
            }
            if (result.andOr !== '') {
              words.push(result.andOr);
            }
          }
        }
      }
    } else if (newValue !== undefined) {
      // Handle value changes
      const isNotInOperator = isPrevNotIn;
      const valueStartIndex = isNotInOperator ? 3 : 2;

      words.splice(valueStartIndex);

      // Check if the newValue contains and/or at the end
      const andOrMatch = newValue.match(/^(.+?)\s+(and|or)$/i);
      if (andOrMatch) {
        // Split the value and and/or parts
        words.push(andOrMatch[1]);
        words.push(andOrMatch[2]);
      } else {
        // Just the value, preserve existing and/or if any
        words.push(newValue);
        if (result.andOr !== '') {
          words.push(result.andOr);
        }
      }
    } else if (newAndOr !== undefined) {
      // Handle adding/changing AND/OR operator only
      const isNotInOperator = isPrevNotIn;
      const valueStartIndex = isNotInOperator ? 3 : 2;

      // Preserve the existing value and only change the AND/OR part
      if (result.value) {
        // We have a value, so preserve it and just update the AND/OR part
        // Remove everything after the value
        const valueWords = result.value.split(' ');
        const endOfValueIndex = valueStartIndex + valueWords.length;
        words.splice(endOfValueIndex);

        // Add the new AND/OR if provided
        if (newAndOr !== '') {
          words.push(newAndOr);
        }
      } else {
        // No value exists yet, so we can't add AND/OR
        // This should not happen in normal flow, but handle gracefully
        if (newAndOr !== '') {
          words.push(newAndOr);
        }
      }
    }

    return words;
  }

function getCurrentItemsFromGroups(groups: Group[]): IFilterPart[] {
  let items: IFilterPart[] = [];
  groups.forEach(group => {
      items.push(...group.items);
      group.children.forEach(child => {
        items.push(...child.items);
      });
  });
  return items;
}

function setItemsBasedOnGroups(groups: Group[]) {
  const items = getCurrentItemsFromGroups(groups);
  setItems(items);
}

const toggleExpand = () => {
  setExpanded(!expanded);
};

const getGroupLabels = (groups: Group[]) => {
  const str = stringifyGroups(groups);
  const filter = str;
  setSource(prevSource => {
      const newSource = { ...prevSource, filter };
      onSourceChange(newSource, partId, props.title);
      return newSource;
  });
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

const formatValueForOperator = (value: string, operator?: string, attributeType?: string): string => {
  if (!value) {
    return value;
  }

  const normalizedOperator = operator?.toUpperCase();
  if (normalizedOperator === "IN" || normalizedOperator === "NOT IN") {
    return ensureInClauseFormat(value);
  }
  return checkType(value, attributeType) ?? value;
};

const ensureInClauseFormat = (value?: string): string => {
  if (!value) {
    return '';
  }

  const trimmedValue = value.trim();
  if (!trimmedValue) {
    return '';
  }

  if (trimmedValue.startsWith('(') && trimmedValue.endsWith(')')) {
    return trimmedValue;
  }

  return `(${trimmedValue})`;
};
const getOptions = (
  attributes?: SqlMembershipAttribute[],
  currentAttributeKey?: string
): IExtendedComboBoxOption[] => {
  let filteredAttributes = attributes || [];

  filteredAttributes = filteredAttributes.filter((attribute) => {
    const attributeKey = attribute.hasMapping ? `${attribute.name}_Code` : attribute.name;
    if (attribute.enabled !== false) {
      // Attribute is enabled, include it
      return true;
    } else if (attributeKey === currentAttributeKey) {
      // Attribute is disabled, but matches the current attribute, include it
      return true;
    } else {
      // Attribute is disabled and does not match the current attribute, exclude it
      return false;
    }
  });

  const options =
    filteredAttributes?.map((attribute) => ({
      key: attribute.hasMapping ? `${attribute.name}_Code` : attribute.name,
      text: attribute.customLabel ? attribute.customLabel : attribute.name,
      description: attribute.description,
      data: { isDisabled: attribute.enabled === false },
    })) || [];
  return options;
};

  const getValueOptions = (attributeMappings?: SqlMembershipAttributeMapping[], selectedKeys?: string[]): IComboBoxOption[] => {
    const valueOptions = attributeMappings?.map(attributeMapping => ({
      key: attributeMapping.code,
      text: attributeMapping.description ? attributeMapping.description : attributeMapping.code
    })) || [];
    const selectedOptions = valueOptions.filter(option => selectedKeys?.includes(option.key));
    const unselectedOptions = valueOptions.filter(option => !selectedKeys?.includes(option.key));
    selectedOptions.sort((a, b) => a.text.localeCompare(b.text));
    unselectedOptions.sort((a, b) => a.text.localeCompare(b.text));
    return [...selectedOptions, ...unselectedOptions];
  };

  useEffect(() => {
    if (props.source.filter) {
      if (containsSqlExpression(props.source.filter)) {
        setFilterTextEnabled(true);
        return;
      }
    }

    if (props.source.filter && !groupingEnabled) {
      let isParsingFilter = false;
      let isParsingGroup = false;
      let numberOfOpenParenthesis = countOccurrences(props.source.filter, "(");
      const numberOfCloseParenthesis = countOccurrences(props.source.filter, ")");
      // Count both IN and NOT IN clauses properly, but exclude quoted values like 'IN'
      const inOperatorRegex = /\s+(NOT\s+)?IN\s+\(/gi;
      const matches = props.source.filter.match(inOperatorRegex) || [];
      const numberOfInClause = matches.length;

      setSelectedKeys([]);
      const hasParentheses = props.source.filter.includes("(") || props.source.filter.includes(")");
      const hasInClause = numberOfInClause > 0;
      if (hasParentheses && hasInClause) {
        if (numberOfOpenParenthesis > numberOfInClause && numberOfCloseParenthesis > numberOfInClause) {
          isParsingGroup = true; // grouping, IN clause
        }

        else if (numberOfOpenParenthesis === numberOfInClause && numberOfCloseParenthesis === numberOfInClause) {
          isParsingFilter = true; // no grouping, only IN clause
        }
      }
      else if (hasParentheses && !hasInClause) {
        isParsingGroup = true; // only grouping, no IN clause
      }
      else {
        isParsingFilter = true; // no grouping, no IN clause
      }

      if (isParsingGroup) {
        const groups = parseGroup(props.source.filter, hasInClause);
        if (groups.length <= 0) {
          setFilterTextEnabled(true);
          return;
        }
        const b = setItemsBasedOnGroups(groups);
        setGroups(groups);
        setGroupingEnabled(true);
      }

      if (isParsingFilter) {
        const regex = new RegExp(`( And | Or | ${PLACEHOLDER_OPERATOR} )`, 'gi');
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
            filter: filter.trim()
          })));
        }
      }
    }
  }, [props.source.filter]);

  useEffect(() => {
    setIsDisabled(!objectIdEmployeeIdMapping);
  }, [objectIdEmployeeIdMapping]);

  useEffect(() => {
    if (orgLeaderUpdated && orgLeaderDetails.employeeId > 0 && partId === orgLeaderDetails.partId) {
      const id: number = orgLeaderDetails.employeeId;
      const newSource = {
        ...props.source,
        manager: {
          ...props.source.manager,
          id: id
        }
      };
      const updatedTitle = localTitle || props.title || "";
      setSource(newSource);
      onSourceChange(newSource, partId, updatedTitle);
    }
  }, [objectIdEmployeeIdMapping]);

  useEffect(() => {
    if (source?.manager?.id) {
      if (objectIdEmployeeIdMapping[source.manager.id] === undefined) {
        dispatch(fetchOrgLeaderDetailsUsingId({
          employeeId: source.manager.id,
          partId: partId as string
        }))
      }
    }
  }, [props.source.manager?.id]);

  useEffect(() => {
    if (orgLeaderDetails.employeeId === 0 && orgLeaderDetails.maxDepth === 0 && includeOrg && partId === orgLeaderDetails.partId) {
      setOrgErrorMessage(hrSource?.name && hrSource?.name !== "" ?
      orgLeaderDetails.text + strings.HROnboarding.customOrgLeaderMissingErrorMessage + (hrSource?.customLabel || hrSource?.name) + strings.HROnboarding.source :
      orgLeaderDetails.text + strings.HROnboarding.orgLeaderMissingErrorMessage);
    }
  }, [orgLeaderDetails]);

  const getSelectedKeys = (input: string): string[] => {
    const matches = input.match(/'([^']+)'|([^(),\s\[\]]+)|\[(.+?)\]|\((.+?)\)/g);
    if (matches) {
        const keys = matches.flatMap(match => {
            const cleaned = match.replace(/'/g, '').trim();
            if (cleaned.startsWith('[') || cleaned.startsWith('(')) {
                return cleaned.slice(1, -1).split(',').map(v => v.trim());
            }
            return [cleaned];
        });
        return keys;
    }
    return [];
  };

  const generateTitle = async () => {
    let generatedTitle = "";
    const orgLeaderPattern = /^(Everyone in .+'s org|\d+\slevels? of direct reports of .+)( with the following summarized criteria: .+)?$/;
    if (source.filter) {
      const result = await dispatch(getTitle(source.filter));
      generatedTitle = result.payload as string;
    }
    let newTitle = props.title;
    if (props.title && orgLeaderPattern.test(props.title)) {
      if (props.title.includes("with the following summarized criteria:")) {
        newTitle = props.title.replace(/with the following summarized criteria: .+/, `with the following summarized criteria: ${generatedTitle}`);
      } else {
          newTitle = `${props.title} with the following summarized criteria: ${generatedTitle}`;
      }
    } else {
      newTitle = generatedTitle;
    }
    onEnableEdit(true);
    onSourceChange(props.source, partId, newTitle);
    setLocalTitle(newTitle);
  };

  const getPickerSuggestions = async (
    filterText: string
  ): Promise<IPersonaProps[]> => {
    return filterText && ownerPickerSuggestions ? ownerPickerSuggestions : [];
  };

  const handleOrgLeaderInputChange = (input: string): string => {
    setIncludeOrg(true);
    setOrgErrorMessage('');
    dispatch(updateJobOwnerFilterSuggestions());
    dispatch(getPeoplePickerSuggestions(input))
    return input;
  }

  const handleOrgLeaderChange = (items?: IPersonaProps[] | undefined) => {
    setIncludeOrg(true);
    setIsDisabled(true);
    if (items !== undefined && items.length > 0) {
      let newSource: HRSourcePartSource = {
        ...props.source,
        manager: {
          ...props.source.manager,
          id: items[0].key as number
        }
      };
      const currentTitle = localTitle || props.title || "";
      const newTitle = updateHRTitleWithNewLeader(
        currentTitle,
        items[0].text as string,
        {
          orgLeaderTitle: strings.HROnboarding.orgLeaderTitle,
          orgLeaderSingleLevelTitle: strings.HROnboarding.orgLeaderSingleLevelTitle,
          orgLeaderMultipleLevelsTitle: strings.HROnboarding.orgLeaderMultipleLevelsTitle,
          withSummarizedCriteria: strings.HROnboarding.withSummarizedCriteria
        }
      );
      setLocalTitle(newTitle);
      onSourceChange(newSource, partId, newTitle);
      dispatch(fetchOrgLeaderDetails({
        objectId: items[0].id as string,
        key: items[0].key as number,
        text: items[0].text as string,
        partId: partId as string
      }));
      setOrgLeaderUpdated(true);
    }
  };

  const handleDepthChange = (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption): void => {
    if (!option) return;

    const depth = parseInt(option.key as string);
    const currentTitle = localTitle || props.title || "";

    // Use the utility function to update depth while preserving leader name and criteria
    const newDepth = depth === 0 ? undefined : depth;
    const newTitle = updateHRTitleWithNewDepth(
      currentTitle,
      newDepth,
      {
        orgLeaderTitle: strings.HROnboarding.orgLeaderTitle,
        orgLeaderSingleLevelTitle: strings.HROnboarding.orgLeaderSingleLevelTitle,
        orgLeaderMultipleLevelsTitle: strings.HROnboarding.orgLeaderMultipleLevelsTitle
      }
    );

    setSource(prevSource => {
      const newSource = {
        ...prevSource,
        manager: {
          ...prevSource.manager,
          depth: newDepth
        }
      };
      onSourceChange(newSource, partId, newTitle);
      return newSource;
    });

    setLocalTitle(newTitle);
  };

  const depthOptions: IDropdownOption[] = [
    { key: '0', text: strings.HROnboarding.all },
  ].concat(
    Array.from({ length: objectIdEmployeeIdMapping[source.manager?.id || 0]?.maxDepth - 1 || 0 }, (_, i) => ({
      key: (i + 2).toString(), // Start keys from 2 to hide level 1 (leader only)
      text: `${i + 1} ${strings.HROnboarding.level}${i + 1 === 1 ? '' : `${strings.HROnboarding.levelsPlural}`} ${strings.HROnboarding.down}`,
    }))
  );

  const handleFilterChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const filter = newValue;
    setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
    });
  };

  const addComponent = (groupIndex?: number, childIndex?: number) => {
    setFilteredOptions({});
    setFilteredValueOptions({});
    setFilterErrorMessage('');
    setSource(props.source);

    if (groupingEnabled) {
      const emptyItemIndex = items.findIndex(item =>
        item.attribute === "" &&
        item.equalityOperator === "" &&
        item.value === "" &&
        item.andOr === ""
      );

      if (emptyItemIndex >= 0) {
        return;
      }

      if (groupIndex != undefined && childIndex !== undefined && groupIndex >= 0 && childIndex >= 0) {
        if (groups[groupIndex].children[childIndex].items[groups[groupIndex].children[childIndex].items.length-1].andOr === "" || groups[groupIndex].children[childIndex].items[groups[groupIndex].children[childIndex].items.length-1].andOr === undefined) {
          setFilterErrorMessage(strings.HROnboarding.missingAttributeErrorMessage);
          return;
        }
      }
      else if (groupIndex !== undefined && groupIndex >= 0) {
        if (groups[groupIndex].items[groups[groupIndex].items.length-1].andOr === "" || groups[groupIndex].items[groups[groupIndex].items.length-1].andOr === undefined) {
          setFilterErrorMessage(strings.HROnboarding.missingAttributeErrorMessage);
          return;
        }
      }

      if (groupIndex !== undefined && childIndex !== undefined && groupIndex >= 0 && childIndex >= 0) {
        groups[groupIndex].children[childIndex].items = [
          ...(groups[groupIndex].children[childIndex].items || []),
          {
            attribute: ``,
            equalityOperator: ``,
            value: ``,
            andOr: ""
          }
        ];
      }

      else if (groupIndex !== undefined && groupIndex >= 0) {
        groups[groupIndex].items = [
          ...(groups[groupIndex].items || []),
          {
            attribute: ``,
            equalityOperator: ``,
            value: ``,
            andOr: ""
          }
        ];
      }

      setGroups(groups);
      setItemsBasedOnGroups(groups);
      return;
    }

    const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
    let segments = props.source.filter?.split(regex);
    let result = true;
    if (segments) {
      for (let i = 0; i < segments.length; i++) {
        const parts = segments[i].trim().split(' ');
        const res = findValueAndOr(parts);
        if (parts[0] === "" || parts[1] === "" || res.value === "" || res.andOr === "") {
            result = false;
            break;
        }
      }
    }
    if (result || children.length === 0) {
    setChildren(prevChildren => [...prevChildren, { filter: ''}]);
    } else {
      setFilterErrorMessage(strings.HROnboarding.missingAttributeErrorMessage);
    }
  };

  const filterItems = (clonedNewGroups: Group[], childItems: IFilterPart[], groupIndex: number, indexToRemove?: number) => {
    if (indexToRemove !== undefined) {
      clonedNewGroups[groupIndex].items = clonedNewGroups[groupIndex].items.filter((_, index) => index !== indexToRemove);

      // Remove trailing AND/OR from the last remaining item in the group
      if (clonedNewGroups[groupIndex].items.length > 0) {
        const lastItemIndex = clonedNewGroups[groupIndex].items.length - 1;
        clonedNewGroups[groupIndex].items[lastItemIndex].andOr = "";
      }
    } else{
      clonedNewGroups[groupIndex].items = clonedNewGroups[groupIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
      !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value && item.andOr === childItem.andOr));
    }

    if (clonedNewGroups[groupIndex].items.length === 0) {
      clonedNewGroups[groupIndex].andOr = "";
      //if last group, delete andOr from previous group / previous group's last child
      if (groupIndex === groups.length-1) {
        const previousGroupIndex = groupIndex - 1;
        if (previousGroupIndex >= 0){
          const prevGroupChildren = clonedNewGroups[previousGroupIndex].children;
          if (prevGroupChildren.length > 0) {
            clonedNewGroups[previousGroupIndex].children[clonedNewGroups[previousGroupIndex].children.length-1].andOr = "";
          }
          else {
            clonedNewGroups[previousGroupIndex].andOr = "";
          }
        }
      }
    }
    return clonedNewGroups;
  };

  const filterChildren = (clonedNewGroups: Group[], childItems: IFilterPart[], groupIndex: number, childIndex: number, indexToRemove?: number) => {
    if (indexToRemove !== undefined) {
      clonedNewGroups[groupIndex].children[childIndex].items = clonedNewGroups[groupIndex].children[childIndex].items.filter((_, index) => index !== indexToRemove);

      // Remove trailing AND/OR from the last remaining item in the group
      if (clonedNewGroups[groupIndex].children[childIndex].items.length > 0) {
        const lastItemIndex = clonedNewGroups[groupIndex].children[childIndex].items.length - 1;
        clonedNewGroups[groupIndex].children[childIndex].items[lastItemIndex].andOr = "";
      }
    } else{
    clonedNewGroups[groupIndex].children[childIndex].items = clonedNewGroups[groupIndex].children[childIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
      !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value && item.andOr === childItem.andOr));
    }
    if (clonedNewGroups[groupIndex].children[childIndex].items.length === 0) {
      clonedNewGroups[groupIndex].children[childIndex].andOr = "";
      if (groupIndex === groups.length-1 && childIndex === clonedNewGroups[groupIndex].children.length-1) {
        //if it's last group and last child, delete andOr from previous child / current group
        const previousChildIndex = childIndex - 1;
        if (previousChildIndex >= 0){
            clonedNewGroups[groupIndex].children[previousChildIndex].andOr = "";
        }
        else {
          clonedNewGroups[groupIndex].andOr = "";
        }
      }
    }
    return clonedNewGroups;
  };

  const removeComponent = (indexToRemove: number, gi?: number, ci?: number) => {
    if (indexToRemove === -1) return;
    if (groupingEnabled) {
      let a: number = -1;
      if ( selectedIndices[0] === -1) {
        const emptyItemIndex = items.findIndex(item =>
          item.attribute === "" &&
          item.equalityOperator === "" &&
          item.value === "" &&
          item.andOr === ""
        );
        a = emptyItemIndex;
      }
      selectedIndices[0] = selectedIndices[0] === -1 ? a : selectedIndices[0];
      let clonedNewGroups: Group[] = JSON.parse(JSON.stringify(groups));
      const selectedItems = items.filter((item, index) => selectedIndices.includes(index));
      const groupIndex = gi ?? groups.findIndex(group =>
        group.children?.some(child =>
            child.items.some(item =>
                JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
            )
        ) || group.items?.some(item =>
            JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
        )
      );
      const childIndex = groupIndex !== -1 ? ci ??groups[groupIndex].children.findIndex(child =>
        child.items.some(item =>
            JSON.stringify(item) === JSON.stringify(items[selectedIndices[0]])
        )
      ) : -1;

      if (groupIndex >= 0 && childIndex < 0 && groups[groupIndex].items[indexToRemove ?? 0]) {
        if (groups[groupIndex].items.length === 1 && groups[groupIndex].children.length > 0) { return; }
        clonedNewGroups = filterItems(clonedNewGroups, selectedItems, groupIndex, indexToRemove);
      }

      else if (childIndex >= 0 && groups[groupIndex].children[childIndex].items[indexToRemove ?? 0]) {
        clonedNewGroups = filterChildren(clonedNewGroups, selectedItems, groupIndex, childIndex, indexToRemove);
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
    }
    else
    {
      setFilteredOptions({});
      setFilteredValueOptions({});
      const updatedChildren = [...children];
      const remainingChildren = children.filter((_, index) => index !== indexToRemove);
      const newFilter = remainingChildren.map(child => child.filter).join(' ').trim();
      const prevIndex = indexToRemove - 1;
      const isSecondLast = prevIndex === children.length - 2;

      if (prevIndex >= 0 && children[prevIndex].filter && isSecondLast) {
        const prevChild = children[prevIndex];
        let words = children[prevIndex].filter.split(' ');

        if (words[0] === "") {
          words = children[prevIndex].filter.trim().split(' ');
        }
        if (words.length > 0) {
          const result = findValueAndOr(words);
          const startIndex = (words.length > 2 && words[1] === "NOT" && words[2] === "IN") ? 3 : 2;
          const indexAfterValue = startIndex + result.value.split(' ').length;
          words.splice(indexAfterValue);
          const newPrevfilter = words.join(' ');
          updatedChildren[prevIndex] = { ...prevChild, filter: newPrevfilter };
          const cleanedFilter = updatedChildren.filter((_, index) => index !== indexToRemove)
                                               .map(child => child.filter)
                                               .join(' ')
                                               .trim();
          setSource(prevSource => {
              const newSource = { ...prevSource, filter: cleanedFilter };
              onSourceChange(newSource, partId, props.title);
              return newSource;
          });
          setChildren(updatedChildren.filter((_, index) => index !== indexToRemove));
          return;
        }
      }

      setSource(prevSource => {
          const newSource = { ...prevSource, filter: newFilter };
          onSourceChange(newSource, partId, props.title);
          return newSource;
      });
      setChildren(prevChildren => prevChildren.filter((_, index) => index !== indexToRemove));
    }
  };

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
      setExpanded(false);
      setFilteredOptions({});
      setFilteredValueOptions({});
      const filter = "";
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      if (children.length === 0) {
        addComponent();
      }
      setIncludeFilter(true);
      setExpanded(true);
      dispatch(fetchDefaultSqlMembershipSourceAttributes());
      setSource(prevSource => {
        const newSource = { ...prevSource };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
  }

  interface UpdateParam {
    property: "attribute" | "value" | "andOr" | "equalityOperator";
    newValue: string;
  }

  const updateGroupItem = (updateParams: UpdateParam | UpdateParam[], index: number, gi?: number, ci?: number): void => {
    const params = Array.isArray(updateParams) ? updateParams : [updateParams];
    const hasDirectContext = gi !== undefined && gi !== null && gi >= 0 && index !== undefined && index !== null;

    if (hasDirectContext && groups[gi as number]) {
      const parentIndex = gi as number;
      const childIndex = ci !== undefined && ci !== null ? ci : -1;

      const updatedGroups = groups.map((group, gIndex) => {
        if (gIndex !== parentIndex) {
          return group;
        }

        if (childIndex !== -1) {
          if (!group.children[childIndex]) {
            return group;
          }

          const updatedChildItems = group.children[childIndex].items.map((item, itemIndex) => {
            if (itemIndex === index) {
              let newItem = { ...item };
              params.forEach(p => {
                newItem = { ...newItem, [p.property]: p.newValue };
              });
              return newItem;
            }
            return item;
          });

          const updatedChild = {
            ...group.children[childIndex],
            items: updatedChildItems
          };

          const updatedChildren = group.children.map((child, childIdx) =>
            childIdx === childIndex ? updatedChild : child
          );

          return {
            ...group,
            children: updatedChildren
          };
        }

        const updatedItems = group.items.map((item, itemIndex) => {
          if (itemIndex === index) {
            let newItem = { ...item };
            params.forEach(p => {
              newItem = { ...newItem, [p.property]: p.newValue };
            });
            return newItem;
          }
          return item;
        });

        return {
          ...group,
          items: updatedItems
        };
      });

      setGroups(updatedGroups);
      const flattenedItems = getCurrentItemsFromGroups(updatedGroups);
      setItems(flattenedItems);
      getGroupLabels(updatedGroups);
      return;
    }
  }

  const getItemFromGroupContext = (groupIndex?: number, childIndex?: number, itemIndex?: number): IFilterPart | undefined => {
    if (
      groupIndex === undefined ||
      groupIndex === null ||
      groupIndex < 0 ||
      itemIndex === undefined ||
      itemIndex === null ||
      itemIndex < 0
    ) {
      return undefined;
    }

    const targetGroup = groups[groupIndex];
    if (!targetGroup) {
      return undefined;
    }

    if (childIndex !== undefined && childIndex !== null && childIndex >= 0) {
      return targetGroup.children?.[childIndex]?.items?.[itemIndex];
    }

    return targetGroup.items?.[itemIndex];
  };

  const handleAttributeChange = (event: React.FormEvent<IComboBox>, item?: IComboBoxOption, index?: number, groupIndex?: number, childIndex?: number): void => {
    if (item) {
      setSelectedKeys([]);
      const selectedAttribute = attributes?.find(({ hasMapping, name }) => ((hasMapping && `${name}_Code` === item.key) || (!hasMapping && name === item.key)));
      if (attributeMappings && attributeMappings[item.key] === undefined) {
        dispatch(fetchAttributeMappings({attribute: item.key as string, type: selectedAttribute?.type, hasMapping: selectedAttribute?.hasMapping }));
      }
      const updatedItems = items.map((it, idx) => {
        if (idx === index) {
          return { ...it, attribute: item.key.toString(), value: '' };
        }
        return it;
      });
      setItems(updatedItems);
    }

    if (groupingEnabled && item && index != null) {
      const updates: UpdateParam[] = [
        { property: "attribute", newValue: item.key.toString() },
        { property: "value", newValue: "" }
      ];
      updateGroupItem(updates, index, groupIndex, childIndex);
      return;
    }

    const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter == undefined || props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      const a = item.key.toString();
      let filter: string;
      if (source.filter && source.filter !== "") {
        filter = `${source.filter} ` + a;
      } else {
        filter = a;
      }
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
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
          const parsed = findValueAndOr(words);
          const isNotIn = words.length > 2 && words[1] === "NOT" && words[2] === "IN";
          const valueStartIndex = isNotIn ? 3 : 2;
          if (parsed.value && words.length > valueStartIndex) {
            const valueWordCount = parsed.value.split(' ').length;
            if (words.length >= valueStartIndex + valueWordCount) {
              words.splice(valueStartIndex, valueWordCount);
            }
          }
        }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
    setFilteredOptions({});
    setFilteredValueOptions({});
  };

  const handleEqualityOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number, groupIndex?: number, childIndex?: number): void => {
    if (groupingEnabled && item && index != null) {
      const currentItem = getItemFromGroupContext(groupIndex, childIndex, index) ?? items[index];
      const prevOperator = currentItem?.equalityOperator;
      const newOperator = item.text;
      const currentValue = currentItem?.value || '';
      
      const isNewIn = newOperator === "IN" || newOperator === "NOT IN";
      const isPrevIn = prevOperator === "IN" || prevOperator === "NOT IN";
      
      let convertedValue = currentValue;
      
      if (isNewIn && !isPrevIn) {
        const currentAttributeKey = currentItem?.attribute;
        const hasMappings = attributeMappings && attributeMappings[currentAttributeKey] && attributeMappings[currentAttributeKey].mappings.length > 0;
        
        if (!hasMappings) {
          convertedValue = "";
        } else if (currentValue) {
          convertedValue = ensureInClauseFormat(currentValue);
        }
      } else if (!isNewIn && isPrevIn && currentValue) {
        if (currentValue.startsWith('(') && currentValue.endsWith(')')) {
          let cleanValue = currentValue.slice(1, -1);
          const firstValue = cleanValue.split(',')[0].trim();
          convertedValue = firstValue;
        }
      }
      
      const updates: UpdateParam[] = [
        { property: "equalityOperator", newValue: item.text }
      ];

      if (convertedValue !== currentValue) {
        updates.push({ property: "value", newValue: convertedValue });
      }
      
      updateGroupItem(updates, index, groupIndex, childIndex);
      return;
    }
    const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
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
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && item) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
        words = updateFilterWords(words, item.text);
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
  };

  const handleAttributeValueChange = (attribute: string, event: React.FormEvent<IComboBox>, existingValues?: string, item?: IComboBoxOption, index?: number, operator?: string, groupIndex?: number, childIndex?: number): void => {
    if ((operator?.toString().toUpperCase() === "IN" || operator?.toString().toUpperCase() === "NOT IN") && (!isJobWriter || !isEditable)) {
      return;
    }
    let selectedValues = "";
    if (operator && (operator.toString().toUpperCase() === "IN" || operator.toString().toUpperCase() === "NOT IN")) {
      let selected = item?.selected;
      if (item) {
        setSelectedKeys(prevSelectedKeys => {
          if (prevSelectedKeys.length === 0 && existingValues && existingValues.length > 0) {
            prevSelectedKeys = getSelectedKeys(existingValues);
          }
          const newSelectedKeys = selected
            ? [...prevSelectedKeys, item!.key as string]
            : prevSelectedKeys.filter(k => k !== item!.key);
          selectedValues = newSelectedKeys.map(key => `'${key}'`).join(', ');
          return newSelectedKeys;
        });
      }
    }

    if (item) {
      const attributeType = attributeMappings[attribute]?.type;
      const selectedValue = operator && (operator.toString().toUpperCase() === "IN" || operator.toString().toUpperCase() === "NOT IN")
        ? ensureInClauseFormat(selectedValues)
        : item.key.toString();
      const selectedValueAfterConversion = formatValueForOperator(selectedValue, operator?.toString(), attributeType);

      if (groupingEnabled && index != null) {
        const updateParams: UpdateParam = {
          property: "value",
          newValue: selectedValueAfterConversion || selectedValue
        };
        updateGroupItem(updateParams, index, groupIndex, childIndex);
        return;
      }

      const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
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
          onSourceChange(newSource, partId, props.title);
          return newSource;
        });
      }
      else if (segments && index !== undefined && segments[index] && item) {
        let words = segments[index].split(' ');
        if (words[0] === "") {
          words = segments[index].trim().split(' ');
        }
        if (words.length > 0) {
          words = updateFilterWords(words, undefined, selectedValueAfterConversion || selectedValue);
        }
        segments[index] = words.join(' ');
        const updatedFilter = segments.join('');
        setSource(prevSource => {
          let filter = updatedFilter;
          const newSource = { ...prevSource, filter };
          onSourceChange(newSource, partId, props.title);
          return newSource;
        });
      }
    }
    setFilteredValueOptions({});
  };

  const handleTAttributeValueChange = (attribute: string, event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '', index: number, operator?: string, groupIndex?: number, childIndex?: number) => {
    const selectedAttribute = attributes?.find(({ hasMapping, name }) => ((hasMapping && `${name}_Code` === attribute) || (!hasMapping && name === attribute)));
    const selectedValue = newValue;
    // Only format if NOT IN/NOT IN, or if it is IN/NOT IN but we are not typing freely (e.g. selection)
    let selectedValueAfterConversion = selectedValue;
    const normalizedOperator = operator?.toUpperCase();
    
    if (normalizedOperator === "IN" || normalizedOperator === "NOT IN") { 
        if (selectedValue.trim().startsWith('(')) {
             selectedValueAfterConversion = selectedValue;
        } else {
             selectedValueAfterConversion = formatValueForOperator(selectedValue, operator?.toString(), selectedAttribute?.type);
        }
    } else {
        selectedValueAfterConversion = formatValueForOperator(selectedValue, operator?.toString(), selectedAttribute?.type);
    }

    const updatedItems = items.map((it, idx) => {
        if (idx === index) {
            return { ...it, value: selectedValueAfterConversion || selectedValue };
        }
        return it;
    });

    setItems(updatedItems);

    if (groupingEnabled && index != null) {
      const updateParams: UpdateParam = {
        property: "value",
        newValue: selectedValueAfterConversion || selectedValue
      };
      updateGroupItem(updateParams, index, groupIndex, childIndex);
      return;
    }
  }

  const handleBlur = (attribute: string, event: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>, index?: number, operator?: string) => {
    if (groupingEnabled && index != null) {
      return;
    }
    const newValue = event.target.value.trim();
    const selectedAttribute = attributes?.find(({ hasMapping, name }) => ((hasMapping && `${name}_Code` === attribute) || (!hasMapping && name === attribute)));
    const selectedValue = newValue;
    const selectedValueAfterConversion = formatValueForOperator(selectedValue, operator?.toString(), selectedAttribute?.type);
    const regex = /(?<= [Aa][Nn][Dd] | [Oo][Rr] )/;
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
          onSourceChange(newSource, partId, props.title);
          return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && selectedValueAfterConversion) {
      let words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
      if (words.length > 0) {
        words = updateFilterWords(words, undefined, selectedValueAfterConversion || selectedValue);
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join('');
      setSource(prevSource => {
        let filter = updatedFilter;
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
  };

  const handleGroupOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, parentIndex: number, childIndex: number, item?: IDropdownOption): void => {
    if (item) {
      if (parentIndex >= 0 && childIndex >= 0) {
        groups[parentIndex].children[childIndex].andOr = item.text;
      }
      if (parentIndex >= 0 && childIndex === -1) {
        groups[parentIndex].andOr = item.text;
      }
      setGroups(groups);
      getGroupLabels(groups);
    }
  }

  const handleOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption, index?: number, groupIndex?: number, childIndex?: number): void => {
    if (groupingEnabled && item && index != null) {
      const updateParams: UpdateParam = {
        property: "andOr",
        newValue: item.text
      };
      updateGroupItem(updateParams, index, groupIndex, childIndex);
      if (item.text !== PLACEHOLDER_OPERATOR) {
        dispatch(setIsMissingAndOrOperator(false));
      }
      return;
    }
    const regex = new RegExp(`(?<= [Aa][Nn][Dd] | [Oo][Rr] | ${PLACEHOLDER_OPERATOR} )`);
    let segments = props.source.filter?.split(regex);
    if (item && (props.source.filter?.length === 0 || (segments?.length == children.length - 1))) {
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + item.text;
      } else {
        filter = item.text;
      }
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && item) {
      let  words = segments[index].split(' ');
      if (words[0] === "") {
        words = segments[index].trim().split(' ');
      }
			if (words.length > 0 && words[words.length - 1] === "") {
				words.pop();
			}
      if (words.length > 0) {
        words = updateFilterWords(words, undefined, undefined, item.text);
      }
      segments[index] = words.join(' ');
      const updatedFilter = segments.join(' ');
      setSource(prevSource => {
        let filter = updatedFilter;
        if (!filter.includes(PLACEHOLDER_OPERATOR)) {
          dispatch(setIsMissingAndOrOperator(false));
        }
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId, props.title);
        return newSource;
      });
    }
  };

  const onAttributeChange = (text: string, index: number, groupIndex?: number, childIndex?: number) => {
    let newFilteredOptions = { ...filteredOptions };
    if (groupingEnabled && groups.length > 0 && groupIndex !== undefined) {
      setItemIndexForAttribute(index);
      setGroupIndexForAttribute(groupIndex);
      setChildIndexForAttribute(childIndex ?? -1);
    }
    if (attributes && attributes.length > 0) {
      const currentAttributeKey = items[index].attribute;
      const options = getOptions(attributes, currentAttributeKey);
      newFilteredOptions[index] = (!text) ? options : options.filter(opt => opt.text.toLowerCase().startsWith(text.toLowerCase()));
      setFilteredOptions(newFilteredOptions);
    }
  };

  const onAttributeValueChange = (text: string, index: number, currentAttributeKey: string, groupIndex?: number, childIndex?: number) => {
    let newFilteredValueOptions = { ...filteredValueOptions };
    if (groupingEnabled && groups.length > 0 && groupIndex !== undefined) {
      setItemIndexForAttributeValue(index);
      setGroupIndexForAttributeValue(groupIndex);
      setChildIndexForAttributeValue(childIndex ?? -1);
    }
    const currentAttributeMappings = attributeMappings[currentAttributeKey].mappings || [];
    if (currentAttributeMappings.length > 0) {
      let valueOptions = getValueOptions(currentAttributeMappings);
      newFilteredValueOptions[index] = (!text) ? valueOptions : valueOptions.filter(opt => opt.text.toLowerCase().startsWith(text.toLowerCase()) || opt.key.toString().toLowerCase().startsWith(text.toLowerCase()));
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
      isResizable: true,
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

  function reorderItems(index: number, newIndex: number, items: IFilterPart[]) {
    let newItems = [...items];
    const insertIndex = newIndex;
    if (insertIndex < 0 || insertIndex >= items.length || index === newIndex) { return; }

    setFilteredOptions({});
    setFilteredValueOptions({});
    setIsDragAndDropEnabled(true);
    newItems = newItems.filter((_, i) => i !== index);
    newItems.splice(insertIndex, 0, { ...items[index] });

    let hasPlaceholder = false;
    newItems = newItems.map((item, itemIndex) => {
      const shouldHaveAndOr = itemIndex < newItems.length - 1;
      const andOrValue = shouldHaveAndOr ? (item.andOr || PLACEHOLDER_OPERATOR) : '';
      if (andOrValue === PLACEHOLDER_OPERATOR) {
        hasPlaceholder = true;
      }
      return {
        ...item,
        andOr: andOrValue
      };
    });

    if (hasPlaceholder) {
      dispatch(setIsMissingAndOrOperator(true));
    }

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

      // Create a new groups array to ensure proper state updates
      const newGroups = [...groups];

      if (groupIndex !== -1 && childIndex === -1) {
        newGroups[groupIndex] = { ...newGroups[groupIndex], items: newItems };
        setGroups(newGroups);
        getGroupLabels(newGroups);
        setItemsBasedOnGroups(newGroups);
      }
      else if (groupIndex !== -1 && childIndex !== -1) {
        const newChildren = [...newGroups[groupIndex].children];
        newChildren[childIndex] = { ...newChildren[childIndex], items: newItems };
        newGroups[groupIndex] = { ...newGroups[groupIndex], children: newChildren };
        setGroups(newGroups);
        getGroupLabels(newGroups);
        setItemsBasedOnGroups(newGroups);
      }
    }
    else {
      const newGroups = [...groups];
      newGroups[0] = { ...newGroups[0], items: newItems };
      setGroups(newGroups);
      setChildren(newChildren);
      setItems(newItems);
    }
  }

  function onUpClick(index: number, items: IFilterPart[]) {
    reorderItems(index, index - 1, items);
  }

  function onDownClick(index: number, items: IFilterPart[]) {
    reorderItems(index, index + 1, items);
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

  const onRenderValueComboBoxOptions = (props?: ISelectableOption, defaultRender?: (props?: ISelectableOption) => JSX.Element | null): JSX.Element | null => {
    return (
      <div className = {classNames.comboBoxOptionContainer}>
        <div>
          <Text>
            {props?.text}
          </Text>
        </div>
        <div>
          <Text variant='tiny' styles={{root: classNames.comboBoxOptionCodeText}}>
           {strings.HROnboarding.valueComboBoxOptionCodeLabel + props?.key}
          </Text>
        </div>
      </div>
    );
  }

  const onRenderAttributeComboBoxOptions = (
    props?: ISelectableOption,
    defaultRender?: (props?: ISelectableOption) => JSX.Element | null
  ): JSX.Element | null => {
    const option = props as IExtendedComboBoxOption;
    if (!option) return null;
    return (
      <div className={classNames.comboBoxOptionContainer}>
        <div>
          <Text>
            {option.text || option.key}
          </Text>
        </div>
        {option.description && (
          <div>
            <Text variant="tiny" styles={{ root: classNames.comboBoxOptionCodeText }}>
              {strings.HROnboarding.descriptionLabel + option.description}
            </Text>
          </div>
        )}
      </div>
    );
  };

  const onRenderOperatorOptions = (
    props?: ISelectableOption,
    defaultRender?: (props?: ISelectableOption) => JSX.Element | null
  ): JSX.Element | null => {
    const option = props as IExtendedComboBoxOption;
    if (!option) return null;
    return (
      <div className={classNames.comboBoxOptionContainer}>
        <div>
          <Text>
            {option.text || option.key}
          </Text>
        </div>
        {option.description && (
          <div>
            <Text variant="tiny" styles={{ root: classNames.comboBoxOptionCodeText }}>
              {strings.HROnboarding.descriptionLabel + option.description}
            </Text>
          </div>
        )}
      </div>
    );
  };

  const onRenderValueComboBoxList: IRenderFunction<ISelectableDroppableTextProps<IComboBox, IComboBox>> = (props, defaultRender) => {
    return (
      <div className = {classNames.comboBoxOptionList}>
        {defaultRender!(props)}
      </div>
    );
  }


  const onRenderDetailsHeader: IRenderFunction<IDetailsHeaderProps> = (
    props,
    defaultRender
  ) => {
    if (!props || !defaultRender) return null;

    const customProps: IDetailsHeaderProps = {
      ...props,
      onRenderColumnHeaderTooltip: (tooltipProps?: IDetailsColumnRenderTooltipProps) => {
        if (!tooltipProps) return null;

        const { column } = tooltipProps;

        if (column?.key === 'andOr') {
          return (
            <span className={classNames.detailsListColumnHeader} >
              <InfoWord
                label={column.name}
                description={<div>
                  <b>{strings.HROnboarding.andOrInfoTitle}</b>
                  <div style={{ marginTop: 8 }}>
                    <p style={{ margin: 0 }}>
                      {jsxFormat(
                          strings.HROnboarding.andLogicDescription,
                          <strong>{strings.HROnboarding.AND}</strong>,
                          <u>{strings.HROnboarding.allLowercase}</u>,
                          <><br /></>,
                          <strong>{strings.HROnboarding.AND}</strong>
                        )}
                    </p>
                    <br />
                    <p style={{ margin: 0 }}>
                    {jsxFormat(
                          strings.HROnboarding.orLogicDescription,
                          <strong>{strings.HROnboarding.OR}</strong>,
                          <u>{strings.HROnboarding.any}</u>,
                          <><br /></>,
                          <strong>{strings.HROnboarding.OR}</strong>
                        )}
                    </p>
                  </div>
                </div>}
                />
            </span>
          );
        }
        else if (column?.key === 'equalityOperator') {
          return (
            <span className={classNames.detailsListColumnHeader} >
              <InfoWord
                label={column.name}
                description={<div>
                  <b>{strings.HROnboarding.equalityOperatorInfoTitle}</b>
                  <div style={{ marginTop: 8 }}>
                    <p style={{ margin: 0 }}>
                      {jsxFormat(
                          strings.HROnboarding.inOperatorDescription,
                          <strong>{strings.HROnboarding.IN}</strong>,
                          <br />
                        )}
                    </p>
                    <br />
                    <p style={{ margin: 0 }}>
                      {jsxFormat(
                          strings.HROnboarding.notInOperatorDescription,
                          <strong>{strings.HROnboarding.NOTIN}</strong>,
                          <br />
                        )}
                    </p>
                    <br />
                    <p style={{ margin: 0 }}>
                    {jsxFormat(
                          strings.HROnboarding.notEqualToOperatorDescription,
                          <strong>{strings.HROnboarding.notEqualTo}</strong>,
                          <br />
                        )}
                    </p>
                  </div>
                </div>}
                />
            </span>
          );
        }
        else {
          return <span className={classNames.detailsListColumnHeader}>{column?.name}</span>;
        }
      },
    };

    return <DetailsHeader {...customProps} />;
  };

  const getOperatorDescription = (operator: string): string => {
    const operatorKey = operator.toUpperCase();
    switch (operatorKey) {
      case '=':
        return strings.HROnboarding.equalToDescription;
      case '<':
        return strings.HROnboarding.lessThanDescription;
      case '<=':
        return strings.HROnboarding.lessThanOrEqualDescription;
      case '>':
        return strings.HROnboarding.greaterThanDescription;
      case '>=':
        return strings.HROnboarding.greaterThanOrEqualDescription;
      case '<>':
        return strings.HROnboarding.notEqualToDescription;
      case 'IN':
        return strings.HROnboarding.inDescription;
      case 'NOT IN':
        return strings.HROnboarding.notInDescription;
      default:
        return '';
    }
  };

  function getValidOperatorsForType(dataType?: string): IExtendedComboBoxOption[] {
    const validOperatorsMap: Record<string, string[]> = {
      bit: ['=', '<>'],
      datetime2: ['=', '<>', '<', '<=', '>', '>=', 'IN', 'NOT IN'],
      float: ['=', '<>', '<', '<=', '>', '>=', 'IN', 'NOT IN'],
      int: ['=', '<>', '<', '<=', '>', '>=', 'IN', 'NOT IN'],
      nvarchar: ['=', '<>', '<', '<=', '>', '>=', 'IN', 'NOT IN']
    };

    const validKeys = dataType ? validOperatorsMap[dataType.toLowerCase()] : undefined;
    const keysToUse = validKeys ?? equalityOperatorOptions.map(option => option.key as string);
    return equalityOperatorOptions
      .filter(option => keysToUse.includes(option.key as string))
      .map(option => ({
        ...option,
        description: getOperatorDescription(option.key as string)
      }));
  }

  const onRenderItemColumn = (items: IFilterPart[], item?: any, index?: number, column?: IColumn, groupIndex?: number, childIndex?: number): JSX.Element => {
    return (
      <HRQueryItemColumn
        items={items}
        item={item}
        index={index}
        column={column}
        groupIndex={groupIndex}
        childIndex={childIndex}
        attributes={attributes}
        attributeMappings={attributeMappings}
        areAttributeMappingsLoading={areAttributeMappingsLoading}
        groupingEnabled={groupingEnabled}
        groups={groups}
        groupIndexForAttribute={groupIndexForAttribute}
        childIndexForAttribute={childIndexForAttribute}
        itemIndexForAttribute={itemIndexForAttribute}
        groupIndexForAttributeValue={groupIndexForAttributeValue}
        childIndexForAttributeValue={childIndexForAttributeValue}
        itemIndexForAttributeValue={itemIndexForAttributeValue}
        filteredOptions={filteredOptions}
        filteredValueOptions={filteredValueOptions}
        isJobWriter={isJobWriter}
        isEditable={isEditable}
        displayEmail={displayEmail}
        onUpClick={onUpClick}
        onDownClick={onDownClick}
        onAttributeChange={onAttributeChange}
        handleAttributeChange={handleAttributeChange}
        handleEqualityOperatorChange={handleEqualityOperatorChange}
        handleAttributeValueChange={handleAttributeValueChange}
        handleTAttributeValueChange={handleTAttributeValueChange}
        handleBlur={handleBlur}
        handleOrAndOperatorChange={handleOrAndOperatorChange}
        removeComponent={removeComponent}
        onAttributeValueChange={onAttributeValueChange}
        getOptions={getOptions}
        getValueOptions={getValueOptions}
        getSelectedKeys={getSelectedKeys}
        checkType={checkType}
        getValidOperatorsForType={getValidOperatorsForType}
        onRenderAttributeComboBoxOptions={onRenderAttributeComboBoxOptions}
        onRenderValueComboBoxOptions={onRenderValueComboBoxOptions}
        onRenderValueComboBoxList={onRenderValueComboBoxList}
        onRenderOperatorOptions={onRenderOperatorOptions}
      />
    );
  };

  function handleSelectionChanged() {
    const selectedItems = selection.getSelection() as IFilterPart[];
    const selectedIndices = selection.getSelectedIndices();
    setSelectedItems(selectedItems);
    setSelectedIndices(selectedIndices);
  }

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
    // Get the most current items from groups instead of relying on potentially stale items state
    const currentItems = getCurrentItemsFromGroups(groups);

    // Recalculate indices based on current items instead of using stale selectedIndices
    const currentSelectedIndices = selectedItems.map(selectedItem => {
      return currentItems.findIndex(item =>
        item.attribute === selectedItem.attribute &&
        item.equalityOperator === selectedItem.equalityOperator &&
        item.value === selectedItem.value
      );
    }).filter(index => index !== -1);

    const si = currentSelectedIndices.includes(-1) ? selectedItems : currentItems.filter((item, index) => currentSelectedIndices.includes(index));
    si.forEach((selectedItem, index) => {
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
    const childItems = si;

    if (allSameGroupIndex && allSameChildIndex && childIndices[0] >= 0) {
      clonedNewGroups[groupIndices[0]].items = [...clonedNewGroups[groupIndices[0]].items, ...childItems];
      clonedNewGroups = filterChildren(clonedNewGroups, si, groupIndices[0], childIndices[0]);
    }

    else if (allSameGroupIndex && groupIndices[0] > 0 && childIndices[0] === -1) {
      // Check if there are children
      const hasChildren = groups[groupIndices[0]] && groups[groupIndices[0]].children && groups[groupIndices[0]].children.length > 0;

      if (hasChildren) {
        // If there are children, check if there are other items besides selected ones
        const hasOtherItems = groups[groupIndices[0]].items.length > si.length;
        if (!hasOtherItems) {
          // If there are no other items and there are children, don't ungroup
          return;
        }
      }

      // If there are no children, or if there are children and other items, ungroup
      clonedNewGroups[0].items = [...clonedNewGroups[0].items, ...childItems];
      clonedNewGroups = filterItems(clonedNewGroups, si, groupIndices[0]);
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
    setSelectedItems([]);
    selection.setAllSelected(false);
    setGroupingEnabled(true);
  }

  function onGroupClick() {
    let newGroups = [...groups];
    let indices: { selectedItemIndex: number, groupIndex: number; childIndex: number }[] = [];
    // Get the most current items from groups instead of relying on potentially stale items state
    const currentItems = getCurrentItemsFromGroups(groups);

    // Recalculate indices based on current items instead of using stale selectedIndices
    const currentSelectedIndices = selectedItems.map(selectedItem => {
      return currentItems.findIndex(item =>
        item.attribute === selectedItem.attribute &&
        item.equalityOperator === selectedItem.equalityOperator &&
        item.value === selectedItem.value
      );
    }).filter(index => index !== -1);

    const si = currentSelectedIndices.includes(-1) ? selectedItems : currentItems.filter((item, index) => currentSelectedIndices.includes(index));
    si.forEach((selectedItem, index) => {
      const groupIndex = groups.findIndex(group => isGroupItem(group, selectedItem));
      if (groupIndex >= 0) {
        indices.push({ selectedItemIndex: index, groupIndex, childIndex: -1 });
      }
    });

    si.forEach((selectedItem, index) => {
      const ifGroupChild = groups.some(group => isGroupChild(group, selectedItem));
      if (ifGroupChild) {
        return;
      }
    });


    // Exit early if no items are eligible for grouping
    if (indices.length === 0) {
      return;
    }

    const groupIndices = indices.map(({ groupIndex }) => groupIndex);
    const allSameGroupIndex = groupIndices.every((groupIndex, index, array) => groupIndex === array[0]);

    let clonedNewGroups: Group[] = JSON.parse(JSON.stringify(newGroups));
    const childItems = si.map(selectedItem => ({ attribute: selectedItem.attribute, equalityOperator: selectedItem.equalityOperator, value: selectedItem.value, andOr: selectedItem.andOr }));
    const filterItems = (groupIndex: number) => {
      clonedNewGroups[groupIndex].items = clonedNewGroups[groupIndex].items.filter((item: { attribute: string; equalityOperator: string; value: string; andOr: string; }) =>
        !childItems.some(childItem => item.attribute === childItem.attribute && item.equalityOperator === childItem.equalityOperator && item.value === childItem.value));
    };

    if (allSameGroupIndex && groupIndices[0] === 0) {
      if (clonedNewGroups[groupIndices[0]].items.length === si.length) { return; }
      const newGroup: Group = {
        name: "",
        items: childItems,
        children: [],
        andOr: ""
      };
      clonedNewGroups.push(newGroup);
      const lastGroup = clonedNewGroups[clonedNewGroups.length - 2];
      lastGroup.children.length > 0
        ? lastGroup.children[lastGroup.children.length - 1].andOr = strings.and // between last child & group i.e. at the end of nested group
        : lastGroup.andOr = strings.and; // between groups
      filterItems(groupIndices[0]);
    }

    else if (allSameGroupIndex && groupIndices[0] > 0) {
      if (clonedNewGroups[groupIndices[0]].items.length === si.length) { return; }
      clonedNewGroups[groupIndices[0]].children = [
        ...(clonedNewGroups[groupIndices[0]].children || []),
        {
          name: ``,
          items: childItems,
          children: [],
          andOr: ""
        }
      ];

      const lastGroupIndex = clonedNewGroups.length - 1;
      const currentGroupIndex = groupIndices[0];
      const currentGroup = clonedNewGroups[currentGroupIndex];

      if (currentGroup.children.length === 1) {
        currentGroup.andOr = strings.and; // between group & first child i.e. at the start of nested group
      } else if (currentGroup.children.length > 1) {
        const lastChildIndex = currentGroup.children.length - 2;
        currentGroup.children[lastChildIndex].andOr = strings.and; // between children
      }

      if (currentGroup.children.length >= 1 && currentGroupIndex < lastGroupIndex) {
        const lastChildIndex = currentGroup.children.length - 1;
        currentGroup.children[lastChildIndex].andOr = strings.and; // between last child & group i.e. at the end of nested group
      }

      filterItems(groupIndices[0]);
    }
    clonedNewGroups = clonedNewGroups.filter((group: { items: string | any[]; children: string | any[]; }) => group.items?.length > 0 || group.children?.length > 0);

    clonedNewGroups.forEach((group: Group) => {
      // Remove andOr from last item in main group items
      if (group.items && group.items.length > 0) {
        group.items[group.items.length - 1].andOr = '';
      }

      // Remove andOr from last item in each child group
      if (group.children && group.children.length > 0) {
        group.children.forEach((child: Group) => {
          if (child.items && child.items.length > 0) {
            child.items[child.items.length - 1].andOr = '';
          }
        });
      }
    });

    setGroups(clonedNewGroups);
    getGroupLabels(clonedNewGroups);
    setSelectedIndices([]);
    setSelectedItems([]);
    selection.setAllSelected(false);
    setGroupingEnabled(true);
    setFilteredOptions({});
    setFilteredValueOptions({});
  }

  const renderItems = (items: IFilterPart[], isUpDownEnabled: boolean, groupIndex: number, childIndex: number) => {
    const selection: Selection = new Selection({
      onSelectionChanged: () => handleSelectionChange(selection)
    });
    return (
      <div>
      {/* {isUpDownEnabled && groups.length > 1 && (<div className={classNames.upDown}>
        <ActionButton iconProps={{ iconName: 'ChevronUp' }} onClick={() => onGroupUpClick(index)} style={{ marginTop: '15px', marginBottom: '-15px'}} />
        <ActionButton iconProps={{ iconName: 'ChevronDown' }} onClick={() => onGroupDownClick(index)} style={{ marginBottom: '-15px'}} />
      </div>)} */}
      <DetailsList
        data-testid="hr-attributes-table"
        styles={{ root: classNames.detailsList }}
        items={items}
        columns={columns}
        onRenderItemColumn={(item, index, column) => onRenderItemColumn(items, item, index, column, groupIndex, childIndex)}
        selection={selection}
        selectionPreservedOnEmptyClick={true}
        layoutMode={DetailsListLayoutMode.justified}
      />
      <ActionButton
        styles={{ root: classNames.addAttribute }}
        iconProps={{ iconName: "CirclePlus" }}
        disabled={!isJobWriter || !isEditable}
        onClick={() => addComponent(groupIndex, childIndex)}>
        {strings.HROnboarding.addAttribute}
      </ActionButton>
      </div>
    );
  };

  const renderGroup = (group: Group, parentIndex: number) => {
    return (
      <div>
      <Stack key={parentIndex}>
        <Stack tokens={{ childrenGap: 10 }}>
          {group.items.length > 0 && renderItems(group.items, true, parentIndex, -1)}
          {((group.items && group.items.length > 0 && parentIndex !== groups.length - 1) || (group.children && group.children.length > 0)) && (
          <div>
          <Dropdown
            onChange={(event, option) => handleGroupOrAndOperatorChange(event, parentIndex, -1, option)}
            selectedKey={group.andOr.charAt(0).toUpperCase() + group.andOr.slice(1).toLowerCase()}
            options={orAndOperatorOptions}
            disabled={!isJobWriter || !isEditable}
            styles={group.children && group.children.length > 0 ?  { root: classNames.startOfNestedGroupDropdown } : { root: classNames.betweenGroupsDropdown }}
            title={strings.HROnboarding.orAndOperator}
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
          {childGroup.items.length > 0 && renderItems(childGroup.items, false, parentIndex, childIndex)}
          {((childGroup.items && childGroup.items.length > 0 && (childIndex !== children.length - 1 || parentIndex !== groups.length - 1)) || (childGroup.children && childGroup.children.length > 0)) && (
          <div>
          <Dropdown
            onChange={(event, option) => handleGroupOrAndOperatorChange(event, parentIndex, childIndex, option)}
            selectedKey={childGroup.andOr.charAt(0).toUpperCase() + childGroup.andOr.slice(1).toLowerCase()}
            options={orAndOperatorOptions}
            disabled={!isJobWriter || !isEditable}
            styles={parentIndex !== groups.length - 1 && childIndex === children.length - 1 ? { root: classNames.endOfNestedGroupDropdown } : { root: classNames.betweenChildrenDropdown }}
            title={strings.HROnboarding.orAndOperator}
          />
          </div>
          )}
          {childGroup.children && renderChildren(childGroup.children, parentIndex)}
        </Stack>
      </Stack>
    ));
  };


  function handleSelectionChange(selection: Selection) {
    const selectedItems = selection.getSelection() as any[];
    // Use current items from groups to get accurate indices
    const currentItems = getCurrentItemsFromGroups(groups);
    const selectedIndices = selectedItems.map(selectedItem => {
      return currentItems.findIndex(item =>
        item.attribute === selectedItem.attribute &&
        item.equalityOperator === selectedItem.equalityOperator &&
        item.value === selectedItem.value &&
        item.andOr === selectedItem.andOr);
    });
    setSelectedIndices(selectedIndices);
    setSelectedItems(selectedItems);
  }

  return (
    <div className={classNames.root}>
      <Label>{strings.HROnboarding.includeOrg}</Label>
      <ChoiceGroup
        data-testid="hr-include-org-choice"
        selectedKey={(includeOrg || source?.manager?.id) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeOrgChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={!isJobWriter || !isEditable}
      />

{(includeOrg || (source?.manager?.id && objectIdEmployeeIdMapping[source.manager.id])) && (
      <Stack horizontal verticalAlign="center" tokens={stackTokens}>
        <Stack.Item align="start">
          <OrgLeader
            selectedItems={source?.manager?.id && objectIdEmployeeIdMapping[source.manager.id] && !isDisabled && orgLeaderDataReturned && orgLeaderDetails.employeeId > 0? [
              {
                key: objectIdEmployeeIdMapping[source.manager.id]?.objectId?.toString() || "",
                text: objectIdEmployeeIdMapping[source.manager.id]?.text?.toString() || ""
              },
            ] : undefined}
            onResolveSuggestions={getPickerSuggestions}
            onInputChange={handleOrgLeaderInputChange}
            onChange={handleOrgLeaderChange}
            disabled={!isJobWriter || !isEditable}
            showError={!!(source?.manager?.id && objectIdEmployeeIdMapping[source.manager.id].text == undefined)}
          />
        </Stack.Item>

        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.depth}</Label>
              <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
                <IconButton title={strings.HROnboarding.depthInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
              </TooltipHost>
            </div>
            <Dropdown
              title={strings.HROnboarding.depth}
              selectedKey={source?.manager?.depth?.toString() ?? '0'}
              onChange={handleDepthChange}
              options={depthOptions}
              styles={{ root: classNames.root, title: classNames.dropdownTitle }}
              disabled={source?.manager?.id == undefined || isDisabled || !isJobWriter || !isEditable}
            />
          </div>
        </Stack.Item>
      </Stack>
       )}


      <div className={classNames.error}>
        {orgLeaderDataReturned && orgLeaderDetails.employeeId === 0 && partId === orgLeaderDetails.partId && orgErrorMessage}
      </div>
      <br />

      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
      <Stack.Item align="start">
      <Label>{format(strings.HROnboarding.includeFilter, hrSource?.customLabel ?? hrSource?.name)}</Label>
      <ChoiceGroup
        data-testid="hr-include-filter-choice"
        selectedKey={(includeFilter || source.filter) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeFilterChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={!isJobWriter || !isEditable}
      />
      </Stack.Item>

      <Stack.Item align="start">
      {(isAITitleEnabled && source.filter) &&
      <div className={classNames.content}>
      <div className={classNames.generateTitleHeader}>
        <div className={classNames.generateTitleButton}>
        <PrimaryButton
          text={strings.HROnboarding.generateTitle}
          onClick={generateTitle}
          disabled={!isJobWriter || !isEditable}
        />
        </div>
        <div className={classNames.generateTitleSpinner}>
        {isGeneratingTitle && (<Spinner size={SpinnerSize.small} label={strings.HROnboarding.generatingTitleText} />)}
        </div>
      </div>
      </div>
      }
      </Stack.Item>
      </Stack>

      {(includeFilter || source.filter) &&
      <div className={classNames.cardHeader}>
        <div className={classNames.cardTitle}>
          {strings.HROnboarding.attributeTitle}
        </div>
        <IconButton
          iconProps={{ iconName: expanded ? 'ChevronUp' : 'ChevronDown' }}
          styles={{ root: classNames.expandButton }}
          onClick={toggleExpand}
          title={expanded ? strings.ManageMembership.labels.collapse : strings.ManageMembership.labels.expand}
          disabled={!isJobWriter}
        />
      </div>}

      {(includeFilter || source.filter) && <Separator styles={{root: classNames.separator}} />}

      {(source.filter && (filterTextEnabled || !attributes)) ?
       (
        <><div className={classNames.labelContainer}>
        <Label>{strings.HROnboarding.filter}</Label>
        <TooltipHost content={strings.HROnboarding.filterInfo} id="toolTipFilterId" calloutProps={{ gapSpace: 0 }}>
          <IconButton title={strings.HROnboarding.filterInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipFilterId" />
        </TooltipHost>
        </div>
        <TextField
          id="filterTextField"
          placeholder={strings.HROnboarding.filterPlaceHolder}
          multiline rows={3}
          resizable={true}
          value={source.filter?.toString()}
          onChange={handleFilterChange}
          styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
          validateOnLoad={false}
          validateOnFocusOut={false}
          disabled={!isJobWriter || !isEditable}
        ></TextField></>
        ) : attributes && attributes.length > 0 && expanded && (includeFilter || source.filter) ?
        (
          <div>
            <ActionButton
              data-testid="hr-group-button"
              iconProps={{ iconName: 'GroupObject' }}
              onClick={onGroupClick}
              disabled={(!(selectedIndices.length > 1)) || !isJobWriter || !isEditable}>
              {strings.HROnboarding.group}
            </ActionButton>
            <ActionButton
              data-testid="hr-ungroup-button"
              iconProps={{ iconName: 'GroupObject' }}
              onClick={onUnGroupClick}
              disabled={(!(selectedIndices.length > 0 && groups.length > 0 && groupingEnabled)) || !isJobWriter || !isEditable}>
              {strings.HROnboarding.ungroup}
            </ActionButton>
          <br/>


          {(groupingEnabled && expanded) ? (

            <div>
              {groups.map((group: Group, index: number) => (
                <div key={`group-${index}`}>
                <React.Fragment key={index}>
                  {renderGroup(group, index)}
                </React.Fragment>
                </div>
              ))}
            </div>
            ) : (

            <DetailsList
              data-testid="hr-attributes-table"
              setKey="items"
              items={items}
              columns={columns}
              onRenderDetailsHeader={onRenderDetailsHeader}
              selectionPreservedOnEmptyClick={true}
              selection={selection}
              onRenderItemColumn={(item, index, column) => onRenderItemColumn(items, item, index, column)}
              layoutMode={DetailsListLayoutMode.justified}
              styles={{
                root: classNames.detailsList
              }}
            />
          )}

          {(!groupingEnabled) &&
          <ActionButton
            data-testid="hr-add-attribute-button"
            styles={{ root: classNames.addAttribute }}
            disabled={!isJobWriter || !isEditable}
            iconProps={{ iconName: "CirclePlus" }}
            onClick={() => addComponent()}>
            {strings.HROnboarding.addAttribute}
          </ActionButton>}
          </div>
        ) : null
      }

      <div className={classNames.error}>
        {filterErrorMessage}
      </div>
    </div>
  );
};
