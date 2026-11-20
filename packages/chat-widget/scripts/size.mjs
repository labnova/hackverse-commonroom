// Prints raw/gzip size of the bundle and fails above the 300 KB gzip budget.
import { readFileSync } from "node:fs";
import { gzipSync } from "node:zlib";

const BUDGET_KB = 300;
const file = new URL("../dist/commonroom-chat.js", import.meta.url);
const raw = readFileSync(file);
const gz = gzipSync(raw, { level: 9 });
const kb = (n) => (n / 1024).toFixed(1);

console.log(`dist/commonroom-chat.js  raw ${kb(raw.length)} KB  gzip ${kb(gz.length)} KB  (budget ${BUDGET_KB} KB)`);
if (gz.length > BUDGET_KB * 1024) {
  console.error(`Bundle exceeds the ${BUDGET_KB} KB gzip budget.`);
  process.exit(1);
}
