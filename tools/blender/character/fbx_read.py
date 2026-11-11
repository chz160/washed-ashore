"""Minimal binary FBX reader for checks that must see the file as Unity does (Blender's importer merges duplicate faces and rescales units).
geometry(path) -> {name: (vertices (N,3) float64, polygons [tuple of vertex indices])} for every Mesh geometry; normals(path) -> the per-polygon-vertex (corner) normals of the first Mesh geometry, (L,3), in polygon order; shapes(path) -> {name: (indices, deltas (K,3))}."""
import struct
import zlib

import numpy as np


def _nodes(data, pos, end, ver):
    nodes = []
    while pos < end:
        if ver >= 7500:
            eo, nprops, plen, nl = struct.unpack_from("<QQQB", data, pos)
            hdr = 25
        else:
            eo, nprops, plen, nl = struct.unpack_from("<IIIB", data, pos)
            hdr = 13
        if eo == 0:
            pos += hdr
            break
        name = data[pos + hdr:pos + hdr + nl].decode("latin1")
        p = pos + hdr + nl
        props = []
        for _ in range(nprops):
            t = chr(data[p])
            p += 1
            if t in "YCFDIL":
                fmt = {"Y": "<h", "C": "<?", "F": "<f", "D": "<d", "I": "<i", "L": "<q"}[t]
                props.append(struct.unpack_from(fmt, data, p)[0])
                p += struct.calcsize(fmt)
            elif t in "fdilb":
                n, enc, cl = struct.unpack_from("<III", data, p)
                p += 12
                raw = data[p:p + cl]
                p += cl
                if enc == 1:
                    raw = zlib.decompress(raw)
                props.append(np.frombuffer(raw, dtype={"f": "<f4", "d": "<f8", "i": "<i4", "l": "<i8", "b": "u1"}[t], count=n))
            elif t in "SR":
                n = struct.unpack_from("<I", data, p)[0]
                p += 4
                props.append(bytes(data[p:p + n]))
                p += n
        children = _nodes(data, p, eo, ver)[0] if p < eo else []
        nodes.append((name, props, children))
        pos = eo
    return nodes, pos


def _load(path):
    data = open(path, "rb").read()
    ver = struct.unpack_from("<I", data, 23)[0]
    return _nodes(data, 27, len(data), ver)[0]


def _geometries(path):
    for n, p, c in _load(path):
        if n == "Objects":
            for gn, gp, gc in c:
                if gn == "Geometry":
                    yield gp, gc


def geometry(path):
    out = {}
    for gp, gc in _geometries(path):
        if gp[2] != b"Mesh":
            continue
        kids = {cn: cp for cn, cp, cc in gc}
        v = np.asarray(kids["Vertices"][0]).reshape(-1, 3)
        idx = np.asarray(kids["PolygonVertexIndex"][0])
        polys, cur = [], []
        for i in idx:
            if i < 0:
                cur.append(int(~i))
                polys.append(tuple(cur))
                cur = []
            else:
                cur.append(int(i))
        out[gp[1].split(b"\x00")[0].decode()] = (v, polys)
    return out


def shapes(path):
    out = {}
    for gp, gc in _geometries(path):
        if gp[2] != b"Shape":
            continue
        kids = {cn: cp for cn, cp, cc in gc}
        out[gp[1].split(b"\x00")[0].decode()] = (np.asarray(kids["Indexes"][0]), np.asarray(kids["Vertices"][0]).reshape(-1, 3))
    return out


def normals(path):
    """Corner normals as written in the file (LayerElementNormal, ByPolygonVertex), (L, 3), in the order of the polygons' vertices (the face loops)."""
    for gp, gc in _geometries(path):
        if gp[2] != b"Mesh":
            continue
        for cn, cp, cc in gc:
            if cn == "LayerElementNormal":
                kids = {k: p for k, p, c in cc}
                mapping = kids.get("MappingInformationType", [b""])[0]
                n = np.asarray(kids["Normals"][0]).reshape(-1, 3)
                if "NormalsIndex" in kids:
                    n = n[np.asarray(kids["NormalsIndex"][0])]
                return n, mapping
    return None, None
