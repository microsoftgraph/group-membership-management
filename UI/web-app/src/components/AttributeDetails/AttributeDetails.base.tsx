// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { classNamesFunction, IconButton, Modal, Separator, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';

import {
  type AttributeDetailsProps,
  type AttributeDetailsStyleProps,
  type AttributeDetailsStyles,
} from './AttributeDetails.types';
import { useStrings } from '../../store/hooks';
import { useSelector } from 'react-redux';
import { selectAttributes } from '../../store/sqlMembershipSources.slice';

const getClassNames = classNamesFunction<AttributeDetailsStyleProps, AttributeDetailsStyles>();

export const AttributeDetailsBase: React.FunctionComponent<AttributeDetailsProps> = (
  props: AttributeDetailsProps
) => {
  const { className, styles} = props;
  const classNames: IProcessedStyleSet<AttributeDetailsStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const attributes = useSelector(selectAttributes);
  const attributesWithDescription = attributes?.filter(attribute => attribute.enabled && attribute.description.trim() !== "");
  const [isModalOpen, setIsModalOpen] = useState(false);

  const handleClick = () => {
    setIsModalOpen(!isModalOpen);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
  };

  return (
    <div className={classNames.root}>
      {strings.HROnboarding.attributeTitle}
      {attributesWithDescription && attributesWithDescription.length > 0 && (
        <>
        <IconButton
          iconProps={{ iconName: 'Info' }}
          onClick={handleClick}
          title={strings.HROnboarding.attributeDescription}
        />
        {isModalOpen && (
          <div>
          <Modal
            isOpen={isModalOpen}
            onDismiss={handleCloseModal}
            styles={{root: classNames.modal}}
          >
            <div className={classNames.close}>
              <IconButton
                iconProps={{ iconName: 'Cancel' }}
                title={strings.close}
                ariaLabel={strings.close}
                onClick={handleCloseModal}
              />
            </div>

            <div className={classNames.content}>
            <div className={classNames.header}>
              <div className={classNames.attributeNameHeader}>
              {strings.HROnboarding.attributeNameHeader}
              </div>
              <div className={classNames.attributeDescriptionHeader}>
              {strings.HROnboarding.attributeDescriptionHeader}
              </div>
            </div>

            <Separator styles={{ root: classNames.headerSeparator }} />

            {attributesWithDescription?.map((attribute) => (
              <div>
              <div key={attribute.name} className={classNames.attributeRow}>
                <div className={classNames.attributeName}>
                  {attribute.name}
                </div>
                <div className={classNames.attributeDescription}>
                  {attribute.description}
                </div>
              </div>
              <div className={classNames.valueSeparator} />
              </div>
            ))}
            </div>

          </Modal>
          </div>
        )}
        </>
        )}
    </div>
  );
};