"""Create a clean, manifold tailgate directly in the BlueTruck FBX.

The source model is one watertight, highly triangulated mesh. A rectangular
Boolean cutter produces two independently watertight objects:

* BlueTruckBody: the truck with a clean tailgate opening and capped cut faces.
* BlueTruckTailgate: the matching solid door with capped edges and hinge origin.

Run from the project root:

    blender --background --factory-startup \
      --python tools/edit_blue_truck_tailgate.py -- \
      --source SourceAssets/BlueTruck/BlueTruck.original.fbx \
      --output Assets/Art/Vehicles/BlueTruck/BlueTruck.fbx \
      --blend SourceAssets/BlueTruck/BlueTruckTailgate.blend
"""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = (
    PROJECT_ROOT / "SourceAssets/BlueTruck/BlueTruck.original.fbx"
)
DEFAULT_OUTPUT = (
    PROJECT_ROOT / "Assets/Art/Vehicles/BlueTruck/BlueTruck.fbx"
)
DEFAULT_BLEND = (
    PROJECT_ROOT / "SourceAssets/BlueTruck/BlueTruckTailgate.blend"
)

# The game scales the original one-metre FBX to the 6.2-metre truck and maps:
#   truck_x = -source_y * SOURCE_SCALE
#   truck_y =  source_z * SOURCE_SCALE - RIDE_HEIGHT
#   truck_z = -source_x * SOURCE_SCALE
SOURCE_SCALE = 6.2002
RIDE_HEIGHT = 0.75
TAILGATE_TRUCK_MIN_Y = 0.14
TAILGATE_TRUCK_MAX_Y = 0.94
TAILGATE_TRUCK_HALF_WIDTH = 1.16
TAILGATE_TRUCK_MAX_X = -2.90
TAILGATE_PIVOT_TRUCK = Vector((-3.04, 0.20, 0.0))

# Extend beyond the rear skin so the cutter intersects the complete closed mesh.
CUTTER_REAR_OVERRUN = 0.025


def parse_args() -> argparse.Namespace:
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--blend", type=Path, default=DEFAULT_BLEND)
    parser.add_argument("--skip-blend", action="store_true")
    return parser.parse_args(arguments)


def import_fbx(path: Path) -> bpy.types.Object:
    if not path.is_file():
        raise FileNotFoundError(f"BlueTruck source FBX does not exist: {path}")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path))

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(
            f"Expected exactly one source mesh, found {[obj.name for obj in meshes]}"
        )

    source = meshes[0]
    source.data.transform(source.matrix_world)
    source.matrix_world = Matrix.Identity(4)
    source.name = "BlueTruckSource"
    source.data.name = "BlueTruckSource"
    return source


def source_tailgate_bounds(source: bpy.types.Object) -> tuple[Vector, Vector]:
    minimum_y = -TAILGATE_TRUCK_MAX_X / SOURCE_SCALE
    maximum_y = max(vertex.co.y for vertex in source.data.vertices)
    minimum_z = (TAILGATE_TRUCK_MIN_Y + RIDE_HEIGHT) / SOURCE_SCALE
    maximum_z = (TAILGATE_TRUCK_MAX_Y + RIDE_HEIGHT) / SOURCE_SCALE
    half_x = TAILGATE_TRUCK_HALF_WIDTH / SOURCE_SCALE
    return (
        Vector((-half_x, minimum_y, minimum_z)),
        Vector((half_x, maximum_y + CUTTER_REAR_OVERRUN, maximum_z)),
    )


def create_box(name: str, minimum: Vector, maximum: Vector) -> bpy.types.Object:
    x0, y0, z0 = minimum
    x1, y1, z1 = maximum
    vertices = [
        (x0, y0, z0),
        (x1, y0, z0),
        (x1, y1, z0),
        (x0, y1, z0),
        (x0, y0, z1),
        (x1, y0, z1),
        (x1, y1, z1),
        (x0, y1, z1),
    ]
    faces = [
        (0, 3, 2, 1),
        (4, 5, 6, 7),
        (0, 1, 5, 4),
        (1, 2, 6, 5),
        (2, 3, 7, 6),
        (3, 0, 4, 7),
    ]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def duplicate_mesh(source: bpy.types.Object, name: str) -> bpy.types.Object:
    duplicate = source.copy()
    duplicate.data = source.data.copy()
    duplicate.name = name
    duplicate.data.name = name
    bpy.context.collection.objects.link(duplicate)
    return duplicate


def apply_boolean(
    target: bpy.types.Object,
    cutter: bpy.types.Object,
    operation: str,
) -> None:
    bpy.context.view_layer.objects.active = target
    target.select_set(True)
    modifier = target.modifiers.new(name=f"Tailgate{operation.title()}", type="BOOLEAN")
    modifier.operation = operation
    modifier.solver = "EXACT"
    modifier.use_hole_tolerant = True
    modifier.object = cutter
    print(f"BOOLEAN {target.name} {operation} start", flush=True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    print(f"BOOLEAN {target.name} {operation} complete", flush=True)
    target.select_set(False)


def repair_boolean_topology(obj: bpy.types.Object) -> None:
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)

    bmesh.ops.dissolve_degenerate(mesh, edges=list(mesh.edges), dist=1e-8)
    zero_area_faces = [face for face in mesh.faces if face.calc_area() < 1e-14]
    if zero_area_faces:
        bmesh.ops.delete(mesh, geom=zero_area_faces, context="FACES")

    boundary_edges = [edge for edge in mesh.edges if edge.is_boundary]
    if boundary_edges:
        bmesh.ops.holes_fill(mesh, edges=boundary_edges, sides=0)

    bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
    mesh.to_mesh(obj.data)
    obj.data.update()
    mesh.free()


def set_tailgate_origin(tailgate: bpy.types.Object) -> None:
    pivot_source = Vector(
        (
            -TAILGATE_PIVOT_TRUCK.z / SOURCE_SCALE,
            -TAILGATE_PIVOT_TRUCK.x / SOURCE_SCALE,
            (TAILGATE_PIVOT_TRUCK.y + RIDE_HEIGHT) / SOURCE_SCALE,
        )
    )
    tailgate.data.transform(Matrix.Translation(-pivot_source))
    tailgate.location = pivot_source


def topology(obj: bpy.types.Object) -> tuple[int, int, int, float]:
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    boundary_edges = sum(1 for edge in mesh.edges if edge.is_boundary)
    non_manifold_edges = sum(
        1 for edge in mesh.edges if len(edge.link_faces) != 2
    )
    result = (
        len(mesh.faces),
        boundary_edges,
        non_manifold_edges,
        mesh.calc_volume(),
    )
    mesh.free()
    return result


def describe_non_manifold_edges(obj: bpy.types.Object) -> None:
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    for edge in [item for item in mesh.edges if len(item.link_faces) != 2][:20]:
        coordinates = [tuple(round(value, 8) for value in vert.co) for vert in edge.verts]
        faces = [
            (round(face.calc_area(), 12), tuple(round(value, 5) for value in face.normal))
            for face in edge.link_faces
        ]
        print(
            f"NON_MANIFOLD {obj.name} edge={coordinates} "
            f"linked_faces={len(edge.link_faces)} faces={faces}",
            flush=True,
        )
    mesh.free()


def has_non_manifold_near_tailgate(obj: bpy.types.Object) -> bool:
    tailgate_minimum_y = -TAILGATE_TRUCK_MAX_X / SOURCE_SCALE
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    result = any(
        len(edge.link_faces) != 2
        and any(vertex.co.y >= tailgate_minimum_y - 0.001 for vertex in edge.verts)
        for edge in mesh.edges
    )
    mesh.free()
    return result


def validate(body: bpy.types.Object, tailgate: bpy.types.Object) -> None:
    for obj in (body, tailgate):
        face_count, boundary_edges, non_manifold_edges, volume = topology(obj)
        print(
            f"TOPOLOGY {obj.name} faces={face_count} boundary={boundary_edges} "
            f"non_manifold={non_manifold_edges} volume={volume:.9f}",
            flush=True,
        )
        invalid = (
            face_count == 0
            or boundary_edges != 0
            or (obj is tailgate and non_manifold_edges != 0)
            or (obj is body and has_non_manifold_near_tailgate(obj))
        )
        if invalid:
            describe_non_manifold_edges(obj)
            raise RuntimeError(f"{obj.name} is not a closed manifold after Boolean")
        if non_manifold_edges:
            print(
                f"WARNING {obj.name} keeps {non_manifold_edges} coincident-surface "
                "edges inherited away from the tailgate; it has no open boundary",
                flush=True,
            )
        if abs(volume) < 1e-8:
            raise RuntimeError(f"{obj.name} has no measurable solid volume")


def save_outputs(
    body: bpy.types.Object,
    tailgate: bpy.types.Object,
    output: Path,
    blend: Path,
    skip_blend: bool,
) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    if not skip_blend:
        blend.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(blend))

    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    tailgate.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        use_space_transform=True,
    )
    print(f"OUTPUT {output}", flush=True)
    if not skip_blend:
        print(f"BLEND {blend}", flush=True)


def main() -> None:
    args = parse_args()
    source = import_fbx(args.source.resolve())
    minimum, maximum = source_tailgate_bounds(source)
    print(f"CUTTER min={tuple(minimum)} max={tuple(maximum)}", flush=True)
    cutter = create_box("TailgateCutter", minimum, maximum)
    body = duplicate_mesh(source, "BlueTruckBody")
    tailgate = duplicate_mesh(source, "BlueTruckTailgate")

    apply_boolean(body, cutter, "DIFFERENCE")
    apply_boolean(tailgate, cutter, "INTERSECT")

    bpy.data.objects.remove(source, do_unlink=True)
    bpy.data.objects.remove(cutter, do_unlink=True)
    repair_boolean_topology(body)
    repair_boolean_topology(tailgate)
    set_tailgate_origin(tailgate)
    validate(body, tailgate)
    save_outputs(
        body,
        tailgate,
        args.output.resolve(),
        args.blend.resolve(),
        args.skip_blend,
    )


if __name__ == "__main__":
    main()
