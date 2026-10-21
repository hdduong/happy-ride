import { expect, test, type Page, type WebSocketRoute } from '@playwright/test';

// Seeded personas: rider An Nguyễn, driver Khoa Phạm.
const riderId = '11111111-1111-1111-1111-111111111111';
const driverId = '33333333-3333-3333-3333-333333333333';
const stepTimeout = 20000;
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
type Activity = {
  activityId: string;
  appSessionId: string;
  eventName: string;
  action?: string;
  rideId?: string;
  connectionId?: string;
};

async function observeActivity(page: Page) {
  const events: Activity[] = [];
  const commands: { action: string; activityId?: string; appSessionId?: string; rideId?: string }[] = [];
  let socket: { client: WebSocketRoute; server: WebSocketRoute } | undefined;
  await page.routeWebSocket(/\/hubs\/ride\?/, (client) => {
    socket = { client, server: client.connectToServer() };
  });
  page.on('response', (response) => {
    const request = response.request();
    if (request.method() !== 'POST' || !response.ok()) return;
    const path = new URL(response.url()).pathname;
    if (path === '/api/telemetry/activity') {
      events.push(request.postDataJSON() as Activity);
    } else if (path.startsWith('/api/rides') || path.startsWith('/api/offers')) {
      const headers = request.headers();
      commands.push({ action: path.split('/').at(-1)!, activityId: headers['x-activity-id'],
        appSessionId: headers['x-app-session-id'], rideId: headers['x-ride-id'] });
    }
  });
  return {
    events, commands,
    async disconnect() {
      expect(socket, 'a real SignalR WebSocket must be connected').toBeDefined();
      const current = socket!;
      // Close only this browser's real connection; keep the API and polling up.
      await Promise.all([current.client.close({ code: 1012 }), current.server.close({ code: 1012 })]);
    },
  };
}

test('customer and driver complete a motorbike ride', async ({ browser, baseURL }) => {
  const driverContext = await browser.newContext({ baseURL });
  const riderContext = await browser.newContext({ baseURL });
  const driver = await driverContext.newPage();
  const rider = await riderContext.newPage();
  const driverActivity = await observeActivity(driver);
  const riderActivity = await observeActivity(rider);

  try {
    await driver.goto(`/driver?as=${driverId}`);
    await expect(driver).toHaveURL(/\/driver/, { timeout: stepTimeout });

    await rider.goto(`/rider?as=${riderId}`);

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

    await expect.poll(() => riderActivity.events.some((event) => event.eventName === 'RideContextStarted')).toBe(true);
    await expect.poll(() => driverActivity.events.some((event) => event.eventName === 'RideContextStarted')).toBe(true);
    const booked = riderActivity.events.find((event) => event.eventName === 'ActionSucceeded' && event.action === 'Book');
    expect(booked?.rideId).toMatch(guid);
    for (const activity of [riderActivity, driverActivity]) {
      await expect.poll(() => activity.events.some((event) => event.eventName === 'ConnectionConnected')).toBe(true);
      const connected = activity.events.find((event) => event.eventName === 'ConnectionConnected')!;
      await activity.disconnect();
      await expect.poll(() => activity.events.some((event) => event.eventName === 'ConnectionReconnected'), {
        timeout: stepTimeout,
      }).toBe(true);
      const reconnecting = activity.events.find((event) => event.eventName === 'ConnectionReconnecting')!;
      const reconnected = activity.events.find((event) => event.eventName === 'ConnectionReconnected')!;
      expect(reconnecting.rideId).toBe(booked!.rideId);
      expect(reconnected.rideId).toBe(booked!.rideId);
      expect(reconnected.appSessionId).toBe(connected.appSessionId);
      expect(reconnected.connectionId).toBeTruthy();
      expect(reconnected.connectionId).not.toBe(connected.connectionId);
    }

    await driver.getByTestId('dropoff').click();

    await rider.getByTestId('confirm-arrival').click();
    await expect(rider.getByTestId('ride-complete')).toBeVisible({ timeout: stepTimeout });

    for (const [activity, actions] of [[riderActivity, ['Book', 'ConfirmArrival']],
      [driverActivity, ['AcceptOffer', 'Arrived', 'Start', 'Dropoff']]] as const) {
      await expect.poll(() => activity.events.some((event) => event.eventName === 'RideContextEnded')).toBe(true);
      for (const action of actions) {
        await expect.poll(() => activity.events.some((event) => event.eventName === 'ActionSucceeded' && event.action === action)).toBe(true);
        const requested = activity.events.find((event) => event.eventName === 'ActionRequested' && event.action === action)!;
        const succeeded = activity.events.find((event) => event.eventName === 'ActionSucceeded' && event.action === action)!;
        expect(succeeded.activityId).toMatch(guid);
        expect(succeeded.activityId).toBe(requested.activityId);
        expect(succeeded.appSessionId).toMatch(guid);
        expect(succeeded.rideId).toBe(booked!.rideId);
        expect(activity.commands.some((command) => command.activityId === succeeded.activityId
          && command.appSessionId === succeeded.appSessionId)).toBe(true);
      }
      expect(activity.events.filter((event) => event.eventName === 'AppOpened')).toHaveLength(1);
      // Only allowlisted identifiers/events cross the telemetry boundary.
      for (const event of activity.events) {
        expect(Object.keys(event).every((key) => ['activityId', 'appSessionId', 'eventName', 'action',
          'rideId', 'offerId', 'connectionId', 'clientOccurredAt'].includes(key))).toBe(true);
      }
    }
  } finally {
    await riderContext.close();
    await driverContext.close();
  }
});
