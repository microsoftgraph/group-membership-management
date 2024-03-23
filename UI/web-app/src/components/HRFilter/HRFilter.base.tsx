// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, Dropdown, IDropdownOption, ActionButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import type {
  HRFilterProps,
  HRFilterStyleProps,
  HRFilterStyles,
} from './HRFilter.types';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { SqlMembershipAttribute } from '../../models';

export const getClassNames = classNamesFunction<HRFilterStyleProps, HRFilterStyles>();

export const HRFilterBase: React.FunctionComponent<HRFilterProps> = (props: HRFilterProps) => {
  const { className, styles, partId, onSourceChange, onRemove, attributes, filter, key, index, childFilters, parentFilter } = props;
  const classNames: IProcessedStyleSet<HRFilterStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const stackTokens: IStackTokens = {
    childrenGap: 30
  };

  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const [selectedAttribute, setSelectedAttribute] = useState<IDropdownOption | undefined>();
  const [selectedEqualityOperator, setSelectedEqualityOperator] = useState<IDropdownOption | undefined>();
  const [selectedAttributeValue, setSelectedAttributeValue] = useState<string>('');
  const [selectedOrAndOperator, setSelectedOrAndOperator] = useState<IDropdownOption | undefined>();
  let options: IDropdownOption[] = [];

  const [dropdownValues, setDropdownValues] = useState({
    dropdownA: '',
    dropdownB: '',
    dropdownC: '',
    dropdownD: '',
  });

  const equalityOperatorOptions: IDropdownOption[] = [
    { key: '=', text: '=' },
    { key: '<', text: '<' },
    { key: '<=', text: '<=' },
    { key: '>', text: '>'},
    { key: '>=', text: '>=' },
    { key: '<>', text: '<>' }
  ];

  const orAndOperatorOptions: IDropdownOption[] = [
    { key: 'Or', text: 'Or' },
    { key: 'And', text: 'And' }
  ];

  useEffect(() => {
   if (filter != undefined)
   {
      const values = filter.split(' ');
      setDropdownValues({
        dropdownA: values[0],
        dropdownB: values[1],
        dropdownC: values[2],
        dropdownD: values[3],
      });
    }
  }, [filter]);

  const getOptions = (attributes: SqlMembershipAttribute[]): IDropdownOption[] => {
    options = attributes?.map((attribute, index) => ({
      key: attribute.name,
      text: attribute.name,
    })) || [];
    return options;
  };

  const removeComponent = (indexToRemove: number) => {
    onRemove();
  };

  const handleAttributeChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setSelectedAttribute(item);
    setDropdownValues({
      dropdownA: "",
      dropdownB: dropdownValues.dropdownB,
      dropdownC: dropdownValues.dropdownC,
      dropdownD: dropdownValues.dropdownD,
    });

    const regex = /(?<= And | Or )/;
    let segments = parentFilter?.split(regex);

    if (item && (parentFilter?.length === 0 || (segments?.length == childFilters.length - 1))) {
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
      const words = segments[index].split(' ');
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

  const handleEqualityOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setSelectedEqualityOperator(item);
    setDropdownValues({
      dropdownA: dropdownValues.dropdownA,
      dropdownB: "",
      dropdownC: dropdownValues.dropdownC,
      dropdownD: dropdownValues.dropdownD,
    });

    const regex = /(?<= And | Or )/;
    let segments = parentFilter?.split(regex);

    if (item && (parentFilter?.length === 0 || (segments?.length == childFilters.length - 1))) {
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
      const words = segments[index].split(' ');
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

  const handleAttributeValueChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    setSelectedAttributeValue(newValue);
    setDropdownValues({
      dropdownA: dropdownValues.dropdownA,
      dropdownB: dropdownValues.dropdownB,
      dropdownC: "",
      dropdownD: dropdownValues.dropdownD,
    });
  };

  const handleBlur = () => {
    const regex = /(?<= And | Or )/;
    let segments = parentFilter?.split(regex);

    if (selectedAttributeValue !== "" && (parentFilter?.length === 0 || (segments?.length == childFilters.length - 1))) {
      let filter: string;
      if (source.filter !== "") {
        filter = `${source.filter} ` + selectedAttributeValue;
      } else {
        filter = selectedAttributeValue;
      }
      setSource(prevSource => {
          const newSource = { ...prevSource, filter };
          onSourceChange(newSource, partId);
          return newSource;
      });
    }
    else if (segments && index !== undefined && segments[index] && selectedAttributeValue) {
      const words = segments[index].split(' ');
      if (words.length > 0) {
          words[2] = selectedAttributeValue;
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

  const handleOrAndOperatorChange = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setSelectedOrAndOperator(item);
    setDropdownValues({
      dropdownA: dropdownValues.dropdownA,
      dropdownB: dropdownValues.dropdownB,
      dropdownC: dropdownValues.dropdownC,
      dropdownD: ""
    });

    const regex = /(?<= And | Or )/;
    let segments = parentFilter?.split(regex);

    if (item && (parentFilter?.length === 0 || (segments?.length == childFilters.length - 1))) {
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
      const words = segments[index].split(' ');
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

  return (
    <div className={classNames.root}>
      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
      <Stack.Item align="start">
        <div>
          <div className={classNames.labelContainer}>
            <Label>Attribute</Label>
            <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id="toolTipOrgLeaderId" calloutProps={{ gapSpace: 0 }}>
              <IconButton title={strings.HROnboarding.orgLeaderInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipOrgLeaderId" />
            </TooltipHost>
          </div>
          <Dropdown
            selectedKey={dropdownValues.dropdownA !== "" ? dropdownValues.dropdownA : selectedAttribute?.key}
            onChange={handleAttributeChange}
            options={attributes.length > 0 ? getOptions(attributes) : []}
            styles={{root: classNames.root, title: classNames.dropdownTitle}}
          />
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <div>
           <div className={classNames.labelContainer}>
            <Label>Equality operator</Label>
            <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
              <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
            </TooltipHost>
          </div>
          <Dropdown
            selectedKey={dropdownValues.dropdownB !== "" ? dropdownValues.dropdownB : selectedEqualityOperator?.key}
            onChange={handleEqualityOperatorChange}
            options={equalityOperatorOptions}
            styles={{root: classNames.root, title: classNames.dropdownTitle}}
          />
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <div>
           <div className={classNames.labelContainer}>
            <Label>Value</Label>
            <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
              <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
            </TooltipHost>
          </div>
          <TextField
            value={dropdownValues.dropdownC !== "" ? dropdownValues.dropdownC : selectedAttributeValue}
            onChange={handleAttributeValueChange}
            onBlur={handleBlur}
            styles={{ fieldGroup: classNames.textField }}
            validateOnLoad={false}
            validateOnFocusOut={false}
       ></TextField>
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <div>
           <div className={classNames.labelContainer}>
            <Label>And/Or</Label>
            <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
              <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
            </TooltipHost>
          </div>
          <Dropdown
            selectedKey={dropdownValues.dropdownD !== "" ? dropdownValues.dropdownD : selectedOrAndOperator?.key}
            onChange={handleOrAndOperatorChange}
            options={orAndOperatorOptions}
            styles={{root: classNames.root, title: classNames.dropdownTitle}}
          />
        </div>
      </Stack.Item>

      <Stack.Item align="start">
          <ActionButton iconProps={{ iconName: "Blocked2" }} onClick={() => removeComponent(key)}>
            Remove
          </ActionButton>
      </Stack.Item>
      </Stack>

      <div className={classNames.error}>
        {errorMessage}
      </div>

    </div>
  );
};