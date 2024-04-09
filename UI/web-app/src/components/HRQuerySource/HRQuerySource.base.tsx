// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption, SpinButton, NormalPeoplePicker, DirectionalHint, ActionButton } from '@fluentui/react';
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
import { selectAttributes } from '../../store/sqlMembershipSources.slice';
import { HRFilter } from '../HRFilter';

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

  const dispatch = useDispatch<AppDispatch>();
  const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
  const objectIdEmployeeIdMapping = useSelector(selectObjectIdEmployeeIdMapping);
  const ownerPickerSuggestions = useSelector(selectJobOwnerFilterSuggestions);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const [isDisabled, setIsDisabled] = useState(true);
  const [includeOrg, setIncludeOrg] = useState(false);
  const [includeFilter, setIncludeFilter] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const [children, setChildren] = useState<ChildType[]>([]);
  const excludeLeaderQuery = `EmployeeId <> ${source.manager?.id}`
  const attributes = useSelector(selectAttributes);

  useEffect(() => {
    if (children.length === 0) {
      setIncludeFilter(false);
    }
  }, [children]);


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
    setChildren(childFilters.map(filter => ({ filter })));
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

  const getPickerSuggestions = async (
    filterText: string
  ): Promise<IPersonaProps[]> => {
    return filterText && ownerPickerSuggestions ? ownerPickerSuggestions : [];
  };

  const handleOrgLeaderInputChange = (input: string): string => {
    dispatch(getJobOwnerFilterSuggestions({displayName: input, alias: input}))
    return input;
  }

  const handleOrgLeaderChange = (items?: IPersonaProps[] | undefined) => {
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
        setErrorMessage(strings.HROnboarding.orgLeaderMissingErrorMessage);
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

      {((source.filter && (source.filter.includes("(") || source.filter.includes(")")))) ?
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
          {children.map((child, index) => (
          <div key={index}>
          <HRFilter
            key={index}
            index={index}
            source={source}
            parentFilter={props.source.filter}
            partId={partId}
            attributes={attributes}
            childFilters={children}
            filter={child.filter}
            onSourceChange={onSourceChange}
            onRemove={() => removeComponent(index)}
          />
          </div>
          ))}
          <ActionButton iconProps={{ iconName: "CirclePlus" }} onClick={addComponent}>
            {strings.HROnboarding.addAttribute}
          </ActionButton>
          </div>
        ) : null
      }

      <div className={classNames.error}>
        {errorMessage}
      </div>

    </div>
  );
};