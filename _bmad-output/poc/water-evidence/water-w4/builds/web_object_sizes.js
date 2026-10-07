// Read-only: per-object sizes of the current web build, uncompressed vs as shipped (w-td, grant #14 follow-up).
// Web.data.unityweb = Brotli(q11) over a UnityWebData container whose data.unity3d is a UnityFS bundle of LZ4HC blocks.
// For every object in sharedassets0.assets / level0 / globalgamemanagers.assets it reports:
//   bytes  - the serialized (uncompressed) object size,
//   lz4    - its share of the packed LZ4HC block bytes (block packed size x overlap / block unpacked size),
//   brotli - its share of the shipped bytes: each block's LZ4HC bytes Brotli-compressed on their own (q11), scaled so the
//            blocks sum to data.unity3d's share of the real Web.data. Shares are estimates; totals are exact.
// Names: the object's leading m_Name string (Texture2D, TerrainData, Mesh, Shader, Material, ...).
// Usage: node web_object_sizes.js <out.json>
const fs = require('fs'), zlib = require('zlib'), C = zlib.constants;
const ROOT = 'E:/GitHub/washed-ashore/';
const packed = fs.readFileSync(ROOT + 'Builds/Web/Build/Web.data.unityweb');
const web = zlib.brotliDecompressSync(packed);
const br = b => zlib.brotliCompressSync(b, { params: { [C.BROTLI_PARAM_QUALITY]: 11, [C.BROTLI_PARAM_SIZE_HINT]: b.length } }).length;

let p = 20, hdr = web.readUInt32LE(16), bundle = null; const container = [];
while (p < hdr) {
  const off = web.readUInt32LE(p), size = web.readUInt32LE(p + 4), nl = web.readUInt32LE(p + 8);
  const name = web.toString('utf8', p + 12, p + 12 + nl); container.push({ name, size });
  if (name === 'data.unity3d') bundle = web.subarray(off, off + size);
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

// UnityFS header and block/node tables.
let q = 0;
const cstr = () => { const e = bundle.indexOf(0, q); const s = bundle.toString('utf8', q, e); q = e + 1; return s; };
cstr(); const ver = bundle.readUInt32BE(q); q += 4; cstr(); cstr(); q += 8;
const cbi = bundle.readUInt32BE(q), ubi = bundle.readUInt32BE(q + 4), flags = bundle.readUInt32BE(q + 8); q += 12;
if (ver >= 7) q = (q + 15) & ~15;
const atEnd = (flags & 0x80) !== 0;
const biRaw = atEnd ? bundle.subarray(bundle.length - cbi) : bundle.subarray(q, q + cbi);
if (!atEnd) q += cbi;
const bi = (flags & 0x3f) === 0 ? biRaw : lz4(biRaw, ubi);
if (flags & 0x200) q = (q + 15) & ~15;
let r = 16; const nBlocks = bi.readUInt32BE(r); r += 4;
const blocks = [];
for (let k = 0; k < nBlocks; k++) { blocks.push({ u: bi.readUInt32BE(r), c: bi.readUInt32BE(r + 4), f: bi.readUInt16BE(r + 8) & 0x3f }); r += 10; }
const nNodes = bi.readUInt32BE(r); r += 4; const nodes = [];
for (let k = 0; k < nNodes; k++) {
  const off = Number(bi.readBigUInt64BE(r)), size = Number(bi.readBigUInt64BE(r + 8)); r += 20;
  const e = bi.indexOf(0, r); nodes.push({ name: bi.toString('utf8', r, e), offset: off, size }); r = e + 1;
}

// Decompress the stream; record each block's stream range, LZ4 bytes and standalone Brotli bytes.
const stream = Buffer.alloc(blocks.reduce((s, b) => s + b.u, 0));
let so = 0, co = q;
for (const b of blocks) {
  const src = bundle.subarray(co, co + b.c);
  const dec = b.f === 0 ? src : lz4(src, b.u);
  dec.copy(stream, so); b.start = so; b.brotli = br(src); so += b.u; co += b.c;
}
const dataShare = br(bundle) ? null : null; // placeholder, real share computed below
const webTotal = packed.length;
// data.unity3d's share of Web.data: total Web.data minus the other container entries compressed on their own (q11).
let others = 0;
p = 20;
while (p < hdr) {
  const off = web.readUInt32LE(p), size = web.readUInt32LE(p + 4), nl = web.readUInt32LE(p + 8);
  const name = web.toString('utf8', p + 12, p + 12 + nl);
  if (name !== 'data.unity3d') others += br(web.subarray(off, off + size));
  p += 12 + nl;
}
const bundleShipped = webTotal - others;
const blockBrotliSum = blocks.reduce((s, b) => s + b.brotli, 0);
const scale = bundleShipped / blockBrotliSum;

function share(a, len) { // stream range -> {lz4, brotli}
  let lzs = 0, brs = 0; const end = a + len;
  let lo = 0, hi = blocks.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (blocks[mid].start <= a) lo = mid; else hi = mid - 1; }
  for (let k = lo; k < blocks.length && blocks[k].start < end; k++) {
    const b = blocks[k], ov = Math.min(end, b.start + b.u) - Math.max(a, b.start);
    if (ov <= 0) continue;
    lzs += b.c * ov / b.u; brs += b.brotli * scale * ov / b.u;
  }
  return { lz4: Math.round(lzs), brotli: Math.round(brs) };
}

const CLASS = { 1: 'GameObject', 4: 'Transform', 21: 'Material', 23: 'MeshRenderer', 28: 'Texture2D', 33: 'MeshFilter', 43: 'Mesh',
  48: 'Shader', 49: 'TextAsset', 74: 'AnimationClip', 83: 'AudioClip', 91: 'AnimatorController', 114: 'MonoBehaviour', 115: 'MonoScript',
  128: 'Font', 156: 'TerrainData', 213: 'Sprite', 238: 'NavMeshData', 1001: 'PrefabInstance' };

const objects = [];
for (const node of nodes) {
  if (!/\.assets$|^level\d+$/.test(node.name)) continue;
  const f = stream.subarray(node.offset, node.offset + node.size);
  const version = f.readUInt32BE(8);
  let pos = 16, little = true, dataOffset;
  if (version >= 22) {
    little = f[pos] === 0; pos += 4; pos += 4; pos += 8; dataOffset = Number(f.readBigUInt64BE(pos)); pos += 16;
  } else { dataOffset = f.readUInt32BE(12); little = f[16] === 0; pos = 20; }
  const u32 = () => { const v = little ? f.readUInt32LE(pos) : f.readUInt32BE(pos); pos += 4; return v; };
  const i32 = () => { const v = little ? f.readInt32LE(pos) : f.readInt32BE(pos); pos += 4; return v; };
  const i16 = () => { const v = little ? f.readInt16LE(pos) : f.readInt16BE(pos); pos += 2; return v; };
  const i64 = () => { const v = little ? f.readBigInt64LE(pos) : f.readBigInt64BE(pos); pos += 8; return Number(v); };
  const e = f.indexOf(0, pos); pos = e + 1; // unity version
  i32(); const typeTree = f[pos++] !== 0;
  if (typeTree) throw new Error(node.name + ' has type trees; parser does not handle them');
  const nTypes = i32(); const types = [];
  for (let k = 0; k < nTypes; k++) {
    const cls = i32(); pos += 1; const script = i16();
    if (cls === 114 || script >= 0) pos += 16; // script id
    pos += 16; // old type hash
    types.push(cls);
  }
  const nObj = i32();
  for (let k = 0; k < nObj; k++) {
    pos = (pos + 3) & ~3;
    i64(); const start = version >= 22 ? i64() : u32(); const size = u32(); const ti = i32();
    const cls = types[ti];
    const at = dataOffset + start;
    let name = '';
    const nl = little ? f.readUInt32LE(at) : f.readUInt32BE(at);
    if (nl > 0 && nl < 200 && at + 4 + nl <= f.length) {
      const s = f.toString('utf8', at + 4, at + 4 + nl); if (/^[\x20-\x7e]+$/.test(s)) name = s;
    }
    const sh = share(node.offset + at, size);
    objects.push({ file: node.name, type: CLASS[cls] || String(cls), name, bytes: size, ...sh });
  }
}

const byType = {};
for (const o of objects) { const t = byType[o.type] || (byType[o.type] = { count: 0, bytes: 0, lz4: 0, brotli: 0 }); t.count++; t.bytes += o.bytes; t.lz4 += o.lz4; t.brotli += o.brotli; }
const top = objects.slice().sort((a, b) => b.brotli - a.brotli).slice(0, 40);
const out = { webDataBytes: webTotal, container, dataUnity3dShippedBytes: bundleShipped, otherContainerEntriesShipped: others,
  bundleUnpacked: stream.length, bundleLz4: blocks.reduce((s, b) => s + b.c, 0), objects: objects.length,
  byType: Object.fromEntries(Object.entries(byType).sort((a, b) => b[1].brotli - a[1].brotli)), topByShipped: top, all: objects };
fs.writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
console.log(JSON.stringify({ ...out, all: undefined }, null, 1));
