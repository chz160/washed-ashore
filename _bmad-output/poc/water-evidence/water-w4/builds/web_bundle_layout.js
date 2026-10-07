// Read-only: reads data.unity3d inside Builds/Web/Build/Web.data.unityweb (a UnityFS bundle) and lists its blocks
// (compression type, packed vs unpacked bytes) and the files inside it with their unpacked sizes.
// Usage: node web_bundle_layout.js <out.json>
const fs = require('fs'), zlib = require('zlib');
const data = zlib.brotliDecompressSync(fs.readFileSync('E:/GitHub/washed-ashore/Builds/Web/Build/Web.data.unityweb'));

// Locate data.unity3d in the UnityWebData1.0 container.
let p = 20, hdr = data.readUInt32LE(16), bundle = null;
while (p < hdr) {
  const off = data.readUInt32LE(p), size = data.readUInt32LE(p + 4), nl = data.readUInt32LE(p + 8);
  if (data.toString('utf8', p + 12, p + 12 + nl) === 'data.unity3d') bundle = data.subarray(off, off + size);
  p += 12 + nl;
}

function lz4(src, outLen) {
  const out = Buffer.alloc(outLen); let i = 0, o = 0;
  while (i < src.length) {
    const tok = src[i++]; let lit = tok >> 4;
    if (lit === 15) { let b; do { b = src[i++]; lit += b; } while (b === 255); }
    src.copy(out, o, i, i + lit); i += lit; o += lit;
    if (i >= src.length) break;
    const off = src[i] | (src[i + 1] << 8); i += 2;
    let m = tok & 15; if (m === 15) { let b; do { b = src[i++]; m += b; } while (b === 255); } m += 4;
    for (let k = 0; k < m; k++, o++) out[o] = out[o - off];
  }
  return out;
}

let q = 0;
const cstr = () => { const e = bundle.indexOf(0, q); const s = bundle.toString('utf8', q, e); q = e + 1; return s; };
const sig = cstr(), ver = bundle.readUInt32BE(q); q += 4; const uver = cstr(), urev = cstr();
const total = Number(bundle.readBigUInt64BE(q)); q += 8;
const cbi = bundle.readUInt32BE(q), ubi = bundle.readUInt32BE(q + 4), flags = bundle.readUInt32BE(q + 8); q += 12;
if (ver >= 7) q = (q + 15) & ~15;
const atEnd = (flags & 0x80) !== 0;
const biRaw = atEnd ? bundle.subarray(bundle.length - cbi) : bundle.subarray(q, q + cbi);
if (!atEnd) q += cbi;
const bi = (flags & 0x3f) === 0 ? biRaw : lz4(biRaw, ubi);
if (flags & 0x200) q = (q + 15) & ~15;
let r = 16; const nBlocks = bi.readUInt32BE(r); r += 4;
const blocks = [];
for (let k = 0; k < nBlocks; k++) { blocks.push({ u: bi.readUInt32BE(r), c: bi.readUInt32BE(r + 4), flags: bi.readUInt16BE(r + 8) }); r += 10; }
const nNodes = bi.readUInt32BE(r); r += 4;
const nodes = [];
for (let k = 0; k < nNodes; k++) {
  const off = Number(bi.readBigUInt64BE(r)), size = Number(bi.readBigUInt64BE(r + 8)); r += 20;
  const e = bi.indexOf(0, r); nodes.push({ name: bi.toString('utf8', r, e), offset: off, bytes: size }); r = e + 1;
}
const types = {};
for (const b of blocks) { const t = ['none', 'lzma', 'lz4', 'lz4hc'][b.flags & 0x3f] || String(b.flags & 0x3f); types[t] = (types[t] || 0) + 1; }
const out = { signature: sig, version: ver, unity: uver, revision: urev, bundleBytes: bundle.length, headerTotal: total, blockCount: nBlocks,
  blockCompression: types, packedBlockBytes: blocks.reduce((s, b) => s + b.c, 0), unpackedBlockBytes: blocks.reduce((s, b) => s + b.u, 0),
  files: nodes.sort((a, b) => b.bytes - a.bytes) };
fs.writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
console.log(JSON.stringify(out, null, 1));
