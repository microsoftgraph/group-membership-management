import { Page } from '@playwright/test';
import { registerMockApiRoutes } from './mockApi';

type SetupMockPageOptions = {
  disclaimerSubmitted?: boolean;
};

export async function setupMockPage(page: Page, options?: SetupMockPageOptions): Promise<void> {
  const disclaimerSubmitted = options?.disclaimerSubmitted ?? true;

  await registerMockApiRoutes(page);

  if (disclaimerSubmitted) {
    await page.addInitScript(() => {
      localStorage.setItem('disclaimerSubmitted', 'true');
    });
  }
}
