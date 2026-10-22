import { inflateSync } from 'node:zlib';
import { expect, test } from '@playwright/test';

test('home map paints streets', async ({ page }, testInfo) => {
  await page.goto('/');
  const map = page.getByTestId('hanoi-map');
  await expect(map).toBeVisible();
  await expect(page.getByTestId('user').first()).toBeVisible();
  await expect(map.locator('.vietmapgl-canvas')).toBeVisible();

  let shot = Buffer.alloc(0);
  await expect.poll(async () => {
    shot = await map.screenshot();
    return centerSpread(shot);
  }, { timeout: 30000 }).toBeGreaterThan(8);

  await testInfo.attach('home-map', { body: shot, contentType: 'image/png' });
  if (process.env.HOME_MAP_SHOT) await import('node:fs').then((fs) => fs.writeFileSync(process.env.HOME_MAP_SHOT!, shot));
});

function centerSpread(png: Buffer) {
  const { width, height, rows } = decodePng(png);
  const x0 = Math.floor(width * 0.3);
  const x1 = Math.floor(width * 0.7);
  const y0 = Math.floor(height * 0.3);
  const y1 = Math.floor(height * 0.65);
  const samples: number[] = [];
  for (let y = y0; y < y1; y += 2) {
    const row = rows[y];
    for (let x = x0; x < x1; x += 2) {
      const i = x * 4;
      samples.push(row[i] * 0.3 + row[i + 1] * 0.59 + row[i + 2] * 0.11);
    }
  }
  const mean = samples.reduce((sum, value) => sum + value, 0) / samples.length;
  const variance = samples.reduce((sum, value) => sum + (value - mean) ** 2, 0) / samples.length;
  return Math.sqrt(variance);
}

function decodePng(png: Buffer) {
  let offset = 8;
  let width = 0;
  let height = 0;
  let colorType = 6;
  const idat: Buffer[] = [];
  while (offset + 8 <= png.length) {
    const length = png.readUInt32BE(offset);
    const type = png.toString('ascii', offset + 4, offset + 8);
    const data = png.subarray(offset + 8, offset + 8 + length);
    if (type === 'IHDR') {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      colorType = data[9];
    } else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    offset += 12 + length;
  }
  const channels = colorType === 6 ? 4 : colorType === 2 ? 3 : 0;
  if (!width || !channels) throw new Error('The map screenshot was not a readable image.');
  const raw = inflateSync(Buffer.concat(idat));
  const stride = width * channels;
  const rows: Buffer[] = [];
  let previous = Buffer.alloc(stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const row = Buffer.alloc(stride);
    const source = raw.subarray(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride);
    for (let i = 0; i < stride; i++) {
      const left = i >= channels ? row[i - channels] : 0;
      const up = previous[i];
      const upLeft = i >= channels ? previous[i - channels] : 0;
      const value = source[i];
      if (filter === 1) row[i] = (value + left) & 255;
      else if (filter === 2) row[i] = (value + up) & 255;
      else if (filter === 3) row[i] = (value + Math.floor((left + up) / 2)) & 255;
      else if (filter === 4) row[i] = (value + paeth(left, up, upLeft)) & 255;
      else row[i] = value;
    }
    if (channels === 3) {
      const rgba = Buffer.alloc(width * 4);
      for (let x = 0; x < width; x++) {
        rgba[x * 4] = row[x * 3];
        rgba[x * 4 + 1] = row[x * 3 + 1];
        rgba[x * 4 + 2] = row[x * 3 + 2];
        rgba[x * 4 + 3] = 255;
      }
      rows.push(rgba);
    } else rows.push(row);
    previous = row;
  }
  return { width, height, rows };
}

function paeth(left: number, up: number, upLeft: number) {
  const estimate = left + up - upLeft;
  const leftDistance = Math.abs(estimate - left);
  const upDistance = Math.abs(estimate - up);
  const upLeftDistance = Math.abs(estimate - upLeft);
  if (leftDistance <= upDistance && leftDistance <= upLeftDistance) return left;
  if (upDistance <= upLeftDistance) return up;
  return upLeft;
}
