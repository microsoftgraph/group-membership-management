// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { execSync } from 'child_process';

export default async function globalTeardown() {
  execSync('npx kill-port 3000');
}