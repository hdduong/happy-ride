import { expect, test } from '@playwright/test';

const riderId = '11111111-1111-1111-1111-111111111111';

test('rider map zoom stays in and shows a person', async ({ page }) => {
  await page.goto(`/rider?as=${riderId}`);
  const canvas = page.locator('.map-canvas');
  await expect(canvas).toHaveAttribute('data-zoom', /[0-9]/, { timeout: 30000 });
  await expect(page.locator('.map-rider svg')).toBeVisible();
  const before = Number(await canvas.getAttribute('data-zoom'));
  const zoomIn = page.locator('.vietmapgl-ctrl-zoom-in');
  await zoomIn.click();
  await zoomIn.click();
  await page.waitForTimeout(3000);
  const after = Number(await canvas.getAttribute('data-zoom'));
  expect(after).toBeGreaterThan(before + 0.5);
  if (process.env.RIDER_MAP_SHOT) await page.screenshot({ path: process.env.RIDER_MAP_SHOT });
});
