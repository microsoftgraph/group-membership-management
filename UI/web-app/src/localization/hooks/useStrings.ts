// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import '../i18n/config';
import { IStrings } from '../IStrings';

export const useStrings = (): IStrings => {
  const { i18n } = useTranslation();
  return i18n.getResourceBundle(i18n.language, 'translations');
};

export default useStrings;
