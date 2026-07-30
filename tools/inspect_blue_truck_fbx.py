"""Print the BlueTruck FBX hierarchy and mesh measurements in Blender space."""

from pathlib import Path

import bpy
import bmesh
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[1]
SOURCE_FBX = PROJECT_ROOT / "Assets/Art/Vehicles/BlueTruck/BlueTruck.fbx"


def import_fbx(path: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=str(path))
    else:
        bpy.ops.import_scene.fbx(filepath=str(path))


def vector_text(values) -> str:
    return "(" + ", ".join(f"{value:.6f}" for value in values) + ")"


def main() -> None:
    import_fbx(SOURCE_FBX)
    print(f"SOURCE {SOURCE_FBX}")
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
        print(
            f"OBJECT {obj.name!r} type={obj.type} "
            f"parent={obj.parent.name if obj.parent else '-'} "
            f"location={vector_text(obj.location)} "
            f"rotation={vector_text(obj.rotation_euler)} "
            f"scale={vector_text(obj.scale)}"
        )
        if obj.type != "MESH":
            continue

        world_points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        minimum = tuple(min(point[index] for point in world_points) for index in range(3))
        maximum = tuple(max(point[index] for point in world_points) for index in range(3))
        print(
            f"MESH vertices={len(obj.data.vertices)} "
            f"polygons={len(obj.data.polygons)} "
            f"materials={len(obj.data.materials)} "
            f"world_min={vector_text(minimum)} "
            f"world_max={vector_text(maximum)}"
        )
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        boundary_edges = sum(1 for edge in mesh.edges if edge.is_boundary)
        manifold_edges = sum(1 for edge in mesh.edges if edge.is_manifold)
        non_manifold_edges = len(mesh.edges) - manifold_edges
        print(
            f"TOPOLOGY edges={len(mesh.edges)} boundary={boundary_edges} "
            f"non_manifold={non_manifold_edges} volume={mesh.calc_volume():.9f}"
        )
        mesh.free()


if __name__ == "__main__":
    main()
