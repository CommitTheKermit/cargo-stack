"""Render closed/open Blender previews for the edited BlueTruck tailgate."""

from __future__ import annotations

from math import radians
from pathlib import Path
import sys

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BLEND = PROJECT_ROOT / "SourceAssets/BlueTruck/BlueTruckTailgate.blend"
DEFAULT_OUTPUT = Path("/tmp/cargo-stack-blender-preview")


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    direction = target - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def material(name: str, color: tuple[float, float, float, float]) -> bpy.types.Material:
    value = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    value.diffuse_color = color
    value.metallic = 0.05
    value.roughness = 0.62
    return value


def render(
    scene: bpy.types.Scene,
    camera: bpy.types.Object,
    tailgate: bpy.types.Object,
    output: Path,
    name: str,
    camera_location: tuple[float, float, float],
    target: tuple[float, float, float],
    open_angle: float,
) -> None:
    tailgate.rotation_euler = (radians(open_angle), 0.0, 0.0)
    camera.location = camera_location
    look_at(camera, Vector(target))
    scene.render.filepath = str(output / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"RENDER {scene.render.filepath}", flush=True)


def main() -> None:
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    blend = Path(arguments[0]).resolve() if arguments else DEFAULT_BLEND
    output = Path(arguments[1]).resolve() if len(arguments) > 1 else DEFAULT_OUTPUT
    output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(blend))

    body = bpy.data.objects["BlueTruckBody"]
    tailgate = bpy.data.objects["BlueTruckTailgate"]
    body.data.materials.clear()
    body.data.materials.append(material("BodyPreview", (0.025, 0.19, 0.48, 1.0)))
    tailgate.data.materials.clear()
    tailgate.data.materials.append(material("TailgatePreview", (0.04, 0.31, 0.78, 1.0)))

    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera_data.lens = 58.0
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.studio_light = "paint.sl"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.curvature_ridge_factor = 1.5
    scene.display.shading.curvature_valley_factor = 1.2
    scene.display.shading.background_type = "WORLD"
    scene.display.shading.show_specular_highlight = True
    if scene.world is None:
        scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.12, 0.16, 0.20)
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False

    render(
        scene,
        camera,
        tailgate,
        output,
        "tailgate-closed-rear",
        (0.0, 1.12, 0.31),
        (0.0, 0.35, 0.18),
        0.0,
    )
    render(
        scene,
        camera,
        tailgate,
        output,
        "tailgate-open-rear",
        (0.0, 1.12, 0.31),
        (0.0, 0.39, 0.16),
        -90.0,
    )
    render(
        scene,
        camera,
        tailgate,
        output,
        "tailgate-open-quarter",
        (-0.62, 0.95, 0.42),
        (0.0, 0.31, 0.17),
        -90.0,
    )


if __name__ == "__main__":
    main()
