import { Page } from '@playwright/test';
import { registerMockApiRoutes } from './mockApi';

type SetupMockPageOptions = {
  disclaimerSubmitted?: boolean;
  createGroupFeatureEnabled?: boolean;
};

export async function setupMockPage(page: Page, options?: SetupMockPageOptions): Promise<void> {
  const disclaimerSubmitted = options?.disclaimerSubmitted ?? true;

  await registerMockApiRoutes(page, options);

  if (disclaimerSubmitted) {
    await page.addInitScript(() => {
      localStorage.setItem('disclaimerSubmitted', 'true');
    });
  }
}
