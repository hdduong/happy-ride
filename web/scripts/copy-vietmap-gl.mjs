import { copyFileSync, mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";

const require = createRequire(import.meta.url);
const dist = join(dirname(require.resolve("@vietmap/vietmap-gl-js/package.json")), "dist");
const out = join(process.cwd(), "public", "vietmap");
mkdirSync(out, { recursive: true });
copyFileSync(join(dist, "vietmap-gl.js"), join(out, "vietmap-gl.js"));
