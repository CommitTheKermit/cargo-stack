#!/usr/bin/env python3
"""Convert a simple embedded GLB cargo model into Unity's built-in OBJ + PNG inputs.

The project intentionally has no glTF package. This converter handles the subset used by
the supplied Tripo cargo assets (one triangle mesh, embedded textures), clusters the very
dense scan mesh, converts glTF's handedness, and keeps the original GLB beside the output.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import shutil
import struct
from array import array
from pathlib import Path

from PIL import Image


JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942


def read_glb(path: Path) -> tuple[dict, bytes]:
    data = path.read_bytes()
    magic, version, declared_size = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF" or version != 2 or declared_size != len(data):
        raise ValueError(f"Unsupported GLB header: {path}")

    document = None
    binary = None
    cursor = 12
    while cursor < len(data):
        length, chunk_type = struct.unpack_from("<II", data, cursor)
        cursor += 8
        chunk = data[cursor:cursor + length]
        cursor += length
        if chunk_type == JSON_CHUNK:
            document = json.loads(chunk.rstrip(b" \t\r\n\0"))
        elif chunk_type == BIN_CHUNK:
            binary = chunk

    if document is None or binary is None:
        raise ValueError("GLB must contain JSON and BIN chunks")
    return document, binary


def read_accessor(document: dict, binary: bytes, index: int) -> tuple[array, int]:
    accessor = document["accessors"][index]
    view = document["bufferViews"][accessor["bufferView"]]
    components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[accessor["type"]]
    type_code, byte_size = {
        5126: ("f", 4),
        5125: ("I", 4),
        5123: ("H", 2),
        5121: ("B", 1),
    }[accessor["componentType"]]
    if view.get("byteStride") not in (None, components * byte_size):
        raise ValueError("Interleaved GLB accessors are not supported")

    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    size = accessor["count"] * components * byte_size
    values = array(type_code)
    values.frombytes(binary[start:start + size])
    if struct.pack("=I", 1) != struct.pack("<I", 1):
        values.byteswap()
    return values, components


def extract_textures(document: dict, binary: bytes, output: Path) -> None:
    embedded: list[bytes] = []
    for image in document.get("images", []):
        view = document["bufferViews"][image["bufferView"]]
        start = view.get("byteOffset", 0)
        embedded.append(binary[start:start + view["byteLength"]])

    if len(embedded) < 3:
        raise ValueError("Expected base-color, metallic-roughness, and normal textures")

    albedo = Image.open(io.BytesIO(embedded[0])).convert("RGB")
    metallic_roughness = Image.open(io.BytesIO(embedded[1])).convert("RGB")
    normal = Image.open(io.BytesIO(embedded[2])).convert("RGB")

    albedo.save(output / "Albedo.png", optimize=True)
    normal.save(output / "Normal.png", optimize=True)

    red, green, blue = metallic_roughness.split()
    smoothness = green.point(lambda value: 255 - value)
    Image.merge("RGBA", (blue, blue, blue, smoothness)).save(
        output / "Metallic.png", optimize=True)
    green.save(output / "Roughness.png", optimize=True)


def cluster_mesh(
    positions: array,
    normals: array,
    uvs: array,
    indices: array,
    position_grid: float,
) -> tuple[list[tuple[float, ...]], list[tuple[int, int, int]]]:
    vertex_count = len(positions) // 3
    if len(normals) // 3 != vertex_count or len(uvs) // 2 != vertex_count:
        raise ValueError("Position, normal, and UV accessor counts must match")

    clusters: dict[tuple[int, ...], int] = {}
    sums: list[list[float]] = []
    remap = array("I", [0]) * vertex_count
    uv_grid = 0.04
    normal_grid = 0.4

    for index in range(vertex_count):
        px, py, pz = positions[index * 3:index * 3 + 3]
        nx, ny, nz = normals[index * 3:index * 3 + 3]
        u, v = uvs[index * 2:index * 2 + 2]
        key = (
            round(px / position_grid),
            round(py / position_grid),
            round(pz / position_grid),
            round(nx / normal_grid),
            round(ny / normal_grid),
            round(nz / normal_grid),
            round(u / uv_grid),
            round(v / uv_grid),
        )
        cluster = clusters.get(key)
        if cluster is None:
            cluster = len(sums)
            clusters[key] = cluster
            sums.append([px, py, pz, nx, ny, nz, u, v, 1.0])
        else:
            target = sums[cluster]
            target[0] += px
            target[1] += py
            target[2] += pz
            target[3] += nx
            target[4] += ny
            target[5] += nz
            target[6] += u
            target[7] += v
            target[8] += 1.0
        remap[index] = cluster

    vertices: list[tuple[float, ...]] = []
    for values in sums:
        count = values[8]
        px, py, pz = (values[i] / count for i in range(3))
        nx, ny, nz = (values[i] / count for i in range(3, 6))
        magnitude = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        u, v = values[6] / count, values[7] / count
        # glTF is right-handed while Unity is left-handed.
        vertices.append((px, py, -pz, nx / magnitude, ny / magnitude, -nz / magnitude, u, v))

    triangles: list[tuple[int, int, int]] = []
    seen: set[tuple[int, int, int]] = set()
    for offset in range(0, len(indices), 3):
        a = remap[indices[offset]]
        b = remap[indices[offset + 1]]
        c = remap[indices[offset + 2]]
        if a == b or b == c or c == a:
            continue
        # Reversing B/C preserves front-face winding after flipping Z.
        triangle = (a, c, b)
        if triangle not in seen:
            seen.add(triangle)
            triangles.append(triangle)
    return vertices, triangles


def write_obj(path: Path, vertices: list[tuple[float, ...]], triangles: list[tuple[int, int, int]]) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as output:
        output.write("# Generated from the adjacent GLB for Unity import\n")
        output.write("o IceCube\n")
        for px, py, pz, *_ in vertices:
            output.write(f"v {px:.7f} {py:.7f} {pz:.7f}\n")
        for *_, u, v in vertices:
            output.write(f"vt {u:.7f} {1.0 - v:.7f}\n")
        for _, _, _, nx, ny, nz, _, _ in vertices:
            output.write(f"vn {nx:.7f} {ny:.7f} {nz:.7f}\n")
        output.write("s 1\n")
        for a, b, c in triangles:
            a += 1
            b += 1
            c += 1
            output.write(f"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--name", default="IceCube")
    parser.add_argument("--grid", type=float, default=0.025)
    args = parser.parse_args()

    args.output.mkdir(parents=True, exist_ok=True)
    document, binary = read_glb(args.source)
    primitive = document["meshes"][0]["primitives"][0]
    if primitive.get("mode", 4) != 4:
        raise ValueError("Only triangle primitives are supported")

    positions, _ = read_accessor(document, binary, primitive["attributes"]["POSITION"])
    normals, _ = read_accessor(document, binary, primitive["attributes"]["NORMAL"])
    uvs, _ = read_accessor(document, binary, primitive["attributes"]["TEXCOORD_0"])
    indices, _ = read_accessor(document, binary, primitive["indices"])
    vertices, triangles = cluster_mesh(positions, normals, uvs, indices, args.grid)

    shutil.copy2(args.source, args.output / f"{args.name}.glb")
    extract_textures(document, binary, args.output)
    write_obj(args.output / f"{args.name}.obj", vertices, triangles)
    print(
        f"Converted {len(positions) // 3:,} vertices / {len(indices) // 3:,} triangles "
        f"to {len(vertices):,} vertices / {len(triangles):,} triangles"
    )


if __name__ == "__main__":
    main()
