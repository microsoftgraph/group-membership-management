// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import {
  IColumn,
  IComboBox,
  IComboBoxOption,
  IDropdownOption,
  ISelectableDroppableTextProps,
  IRenderFunction
} from '@fluentui/react';
import { IFilterPart } from '../../../models/IFilterPart';
import { SqlMembershipAttribute, SqlMembershipAttributeMapping } from '../../../models';

export type HRQueryItemColumnStyles = {
  root: IStyle;
  upDown: IStyle;
  attribute: IStyle;
  equalityOperator: IStyle;
  value: IStyle;
  andOr: IStyle;
  remove: IStyle;
  removeButton: IStyle;
  removeButtonDisabled: IStyle;
  textField: IStyle;
  dropdownTitle: IStyle;
  errorMessageStyles: IStyle;
  comboBoxOptionCodeText: IStyle;
  comboBoxOptionContainer: IStyle;
  comboBoxOptionList: IStyle;
  readOnlyComboBox: IStyle;
  readOnlyComboBoxInput: IStyle;
  operatorDescription: IStyle;
};

export type HRQueryItemColumnStyleProps = {
  className?: string;
  theme: ITheme;
};

export type HRQueryItemColumnProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<HRQueryItemColumnStyleProps, HRQueryItemColumnStyles>;

  // Core props
  items: IFilterPart[];
  item?: any;
  index?: number;
  column?: IColumn;
  groupIndex?: number;
  childIndex?: number;

  // State dependencies
  attributes?: SqlMembershipAttribute[];
  attributeMappings: Record<string, { mappings: SqlMembershipAttributeMapping[]; type?: string }>;
  areAttributeMappingsLoading: boolean;
  groupingEnabled: boolean;
  groups: any[];
  groupIndexForAttribute: number;
  childIndexForAttribute: number;
  itemIndexForAttribute: number;
  groupIndexForAttributeValue: number;
  childIndexForAttributeValue: number;
  itemIndexForAttributeValue: number;
  filteredOptions: Record<number, IComboBoxOption[]>;
  filteredValueOptions: Record<number, IComboBoxOption[]>;
  isJobWriter: boolean;
  isEditable?: boolean;
  displayEmail: string;

  // Callbacks
  onUpClick: (index: number, items: IFilterPart[]) => void;
  onDownClick: (index: number, items: IFilterPart[]) => void;
  onAttributeChange: (text: string, index: number, groupIndex?: number, childIndex?: number) => void;
  handleAttributeChange: (event: React.FormEvent<IComboBox>, option?: IComboBoxOption, index?: number, groupIndex?: number, childIndex?: number) => void;
  handleEqualityOperatorChange: (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption, index?: number, groupIndex?: number, childIndex?: number) => void;
  handleAttributeValueChange: (attribute: string, event: React.FormEvent<IComboBox>, existingValues?: string, option?: IComboBoxOption, index?: number, operator?: string, groupIndex?: number, childIndex?: number) => void;
  handleTAttributeValueChange: (attribute: string, event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string, index: number, operator?: string, groupIndex?: number, childIndex?: number) => void;
  handleBlur: (attribute: string, event: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>, index?: number, operator?: string) => void;
  handleOrAndOperatorChange: (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption, index?: number, groupIndex?: number, childIndex?: number) => void;
  removeComponent: (indexToRemove: number, groupIndex?: number, childIndex?: number) => void;
  onAttributeValueChange: (text: string, index: number, currentAttributeKey: string, groupIndex?: number, childIndex?: number) => void;
  getOptions: (attributes?: SqlMembershipAttribute[], currentAttributeKey?: string) => IComboBoxOption[];
  getValueOptions: (attributeMappings?: SqlMembershipAttributeMapping[], selectedKeys?: string[]) => IComboBoxOption[];
  getSelectedKeys: (input: string) => string[];
  checkType: (value: string, type: string | undefined) => string;
  getValidOperatorsForType: (dataType?: string) => IDropdownOption[];
  onRenderAttributeComboBoxOptions: (props?: IComboBoxOption, defaultRender?: (props?: IComboBoxOption) => JSX.Element | null) => JSX.Element | null;
  onRenderValueComboBoxOptions: (props?: IComboBoxOption, defaultRender?: (props?: IComboBoxOption) => JSX.Element | null) => JSX.Element | null;
  onRenderValueComboBoxList: IRenderFunction<ISelectableDroppableTextProps<IComboBox, IComboBox>>;
};
