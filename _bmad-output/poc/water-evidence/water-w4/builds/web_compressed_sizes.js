// Read-only: estimates the Brotli-compressed bytes each part of the web build adds (w-td, grant #14 follow-up).
// 1. Decompresses Builds/Web/Build/Web.data.unityweb, re-compresses it at Brotli q9/q11 to find the build's level.
// 2. Splits the UnityWebData container into its files and Brotli-compresses each (per-container ratios).
// 3. Compresses WaterMaps' raw RG16 bytes (from waterMaps.raw, written by the Python step) and every source file given
//    on the command line, at the matched level, so packed/uncompressed and compressed sizes sit side by side.
// Usage: node web_compressed_sizes.js <out.json> [raw files...]
const fs = require('fs'), zlib = require('zlib');
const root = 'E:/GitHub/washed-ashore/';
const C = zlib.constants;
const br = (buf, q) => zlib.brotliCompressSync(buf, { params: { [C.BROTLI_PARAM_QUALITY]: q, [C.BROTLI_PARAM_SIZE_HINT]: buf.length } }).length;

const packed = fs.readFileSync(root + 'Builds/Web/Build/Web.data.unityweb');
const data = zlib.brotliDecompressSync(packed);
const out = { webDataOnDisk: packed.length, webDataDecompressed: data.length };
for (const q of [6, 9, 11]) out['recompressed_q' + q] = br(data, q);
const level = [6, 9, 11].reduce((b, q) => Math.abs(out['recompressed_q' + q] - packed.length) < Math.abs(out['recompressed_q' + b] - packed.length) ? q : b, 11);
out.matchedQuality = level;

// UnityWebData1.0 container: magic, uint32 header size, then per file uint32 offset, uint32 size, uint32 name length, name.
const magic = data.toString('ascii', 0, 16);
out.container = magic.replace(/\0/g, '');
let p = 16; const headerSize = data.readUInt32LE(p); p += 4;
const files = [];
while (p < headerSize) {
  const off = data.readUInt32LE(p), size = data.readUInt32LE(p + 4), nl = data.readUInt32LE(p + 8);
  const name = data.toString('utf8', p + 12, p + 12 + nl); p += 12 + nl;
  const slice = data.subarray(off, off + size);
  files.push({ name, bytes: size, brotli: br(slice, level) });
}
files.sort((a, b) => b.bytes - a.bytes);
out.files = files.map(f => ({ ...f, ratio: +(f.brotli / Math.max(1, f.bytes)).toFixed(3) }));

out.sources = [];
for (const f of process.argv.slice(3)) {
  const buf = fs.readFileSync(f);
  out.sources.push({ file: f.replace(root, ''), bytes: buf.length, brotli: br(buf, level), ratio: +(br(buf, level) / buf.length).toFixed(3) });
}
fs.writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
console.log(JSON.stringify(out, null, 1));
