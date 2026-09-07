@tool
class_name PixelOutlinePalette
extends Resource
## Per-ID outline colors for the 256-entry outline palette.
##
## Row 0 of the baked 256x2 HDR texture holds the EXTERIOR (silhouette/ID
## edge) color per outline ID, row 1 the INTERIOR (crease) color; alpha
## carries the multiply flag (1 = multiply with the scene color, 0 = flat).
## IDs are 1-255 (0 = background, never outlined). Any ID missing from the
## dictionaries falls back to [member fallback_exterior]/[member fallback_interior].

## Exterior (silhouette) color per outline ID.
@export var exterior_colors := {
	1: Color(0.0, 0.0, 0.0, 1.0),
}
## When true for an ID, the exterior color multiplies the scene color
## (tinted outline) instead of replacing it flat.
@export var multiply_exterior := {}
## Interior (crease) color per outline ID.
@export var interior_colors := {
	1: Color(0.5, 0.5, 0.5, 1.0),
}
## When true for an ID, the interior color tints (multiplies) instead of
## replacing the shaded color flat.
@export var multiply_interior := {}

## Fallback exterior color for IDs not in [member exterior_colors].
@export var fallback_exterior := Color(0.0, 0.0, 0.0, 1.0)
## Whether the fallback exterior multiplies.
@export var fallback_multiply_exterior := false
## Fallback interior color for IDs not in [member interior_colors].
@export var fallback_interior := Color(0.5, 0.5, 0.5, 1.0)
## Whether the fallback interior multiplies.
@export var fallback_multiply_interior := true


func exterior_for(id: int) -> Color:
	return exterior_colors.get(id, fallback_exterior)


func interior_for(id: int) -> Color:
	return interior_colors.get(id, fallback_interior)


func multiplies_exterior(id: int) -> bool:
	return bool(multiply_exterior.get(id, fallback_multiply_exterior))


func multiplies_interior(id: int) -> bool:
	return bool(multiply_interior.get(id, fallback_multiply_interior))


## Bakes the 256x2 RGBAH image (row 0 = exterior, row 1 = interior,
## alpha = multiply flag) as a 16-bit float RGBA LUT.
func bake_image() -> Image:
	var img := Image.create(256, 2, false, Image.FORMAT_RGBAH)
	for id in 256:
		var ext := exterior_for(id)
		var interior := interior_for(id)
		ext.a = 1.0 if multiplies_exterior(id) else 0.0
		interior.a = 1.0 if multiplies_interior(id) else 0.0
		img.set_pixel(id, 0, ext)
		img.set_pixel(id, 1, interior)
	return img


## Raw bytes for the RD texture upload (256 * 2 RGBA half-floats).
func bake_bytes() -> PackedByteArray:
	return bake_image().get_data()


## Default LUT bytes: black flat exterior, neutral multiply interior.
static func default_bytes() -> PackedByteArray:
	var img := Image.create(256, 2, false, Image.FORMAT_RGBAH)
	img.fill(Color(0.0, 0.0, 0.0, 0.0))
	for id in 256:
		img.set_pixel(id, 1, Color(0.5, 0.5, 0.5, 1.0))
	return img.get_data()
