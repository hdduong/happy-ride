import { expect, test } from '@playwright/test';

// Seeded personas: rider An Nguyễn, driver Khoa Phạm.
const riderId = '11111111-1111-1111-1111-111111111111';
const driverId = '33333333-3333-3333-3333-333333333333';
const stepTimeout = 20000;

test('customer and driver complete a motorbike ride', async ({ browser }) => {
  const driverContext = await browser.newContext();
  const riderContext = await browser.newContext();
  const driver = await driverContext.newPage();
  const rider = await riderContext.newPage();

  try {
    await driver.goto(`http://localhost:3000/driver?as=${driverId}`);
    await expect(driver).toHaveURL(/\/driver/, { timeout: stepTimeout });

    await rider.goto(`http://localhost:3000/rider?as=${riderId}`);

    await rider.getByTestId('where-to').fill('bitexco');
    await rider.getByTestId('place-result').filter({ hasText: 'Bitexco' }).click();
    await rider.getByTestId('book-motorbike').click();

    await expect(rider.getByTestId('finding-driver')).toBeVisible({ timeout: stepTimeout });

    await expect(driver.getByTestId('offer-card')).toBeVisible({ timeout: stepTimeout });
    await driver.getByTestId('accept-ride').click();

    await expect(rider.getByTestId('driver-card')).toContainText('59-H1 234.56', {
      timeout: stepTimeout,
    });

    await driver.getByTestId('arrived').click();
    await driver.getByTestId('pickup').click();
    await driver.getByTestId('dropoff').click();

    await rider.getByTestId('confirm-arrival').click();
    await expect(rider.getByTestId('ride-complete')).toBeVisible({ timeout: stepTimeout });
  } finally {
    await riderContext.close();
    await driverContext.close();
  }
});
