@tool
class_name PixelPalette
extends Resource
## Bakes a 256x256 palette LUT used for per-object color grading.
##
## Port of ProPixelizer's Tools/Palette.cs. The LUT layout matches the
## original: x = r + 16 * b, y = g + 16 * ditherBand, with 16 dither bands.
## Runtime only needs the baked PNG (see the material's palette_lut uniform).

## How palette matches are scored.
enum ColorMethod {
	NEAREST_RGB, ## Nearest palette color in RGB space.
	NEAREST_HSV, ## Nearest in HSV space (squared terms).
	WEIGHTED_HSV, ## HSV, weighted by [member weights].
	WEIGHTED_HSV_SQUARED, ## HSV squared distance, weighted.
	NEAREST_VALUE, ## Nearest by Value after mapping input through [member v_conversion_curve].
}

const RESOLUTION := 16
const DITHER_PATTERN_SIZE := 16

## Source texture holding the palette colors (every distinct pixel is a swatch).
@export var source: Texture2D

@export var method: ColorMethod = ColorMethod.NEAREST_HSV

## Weights for hue / saturation / brightness (WEIGHTED_HSV* methods).
@export var weights := Vector3.ONE

## Maps input Value to comparison Value (V_NEAREST only).
@export var v_conversion_curve: Curve

@export var use_dither_pattern := true

## Dither pattern used when baking the LUT bands.
@export var dither_pattern: PixelDitherPattern

## Where the baked PNG is written.
@export_file("*.png") var output_path := "res://palette_LUT.png"

@export_tool_button("Bake LUT", "VisualShader") var bake_button := bake


class ColorSwatches:
	## The distinct colors of the source palette image.
	var colors: Array[Color] = []

	func _init(palette_image: Image) -> void:
		var seen := {}
		for y in palette_image.get_height():
			for x in palette_image.get_width():
				var c: Color = palette_image.get_pixel(x, y)
				var key := c.to_rgba32()
				if not seen.has(key):
					seen[key] = true
					colors.append(c)
		if colors.is_empty():
			push_error("PixelPalette: source texture has no pixels.")
			colors.append(Color.MAGENTA)

	static func _brightness(c: Color) -> float:
		return c.r * 0.3 + c.g * 0.59 + c.b * 0.11

	func color_distance(a: Color, b: Color, p_method: ColorMethod, p_weights: Vector3, curve: Curve) -> float:
		# Compare in gamma space (source should be an sRGB texture).
		var hue_diff: float = absf(a.h - b.h)
		hue_diff = 1.0 - hue_diff if hue_diff > 0.5 else hue_diff
		var bright_a := _brightness(a)
		var bright_b := _brightness(b)
		match p_method:
			ColorMethod.NEAREST_HSV:
				return pow(hue_diff, 2.0) + pow(bright_a - bright_b, 2.0) + pow(a.s - b.s, 2.0)
			ColorMethod.WEIGHTED_HSV:
				return p_weights.x * hue_diff + p_weights.y * absf(a.s - b.s) + p_weights.z * absf(bright_a - bright_b)
			ColorMethod.WEIGHTED_HSV_SQUARED:
				return 4.0 * p_weights.x * pow(hue_diff, 2.0) + p_weights.y * pow(a.s - b.s, 2.0) + p_weights.z * pow(bright_a - bright_b, 2.0)
			ColorMethod.NEAREST_RGB:
				return Vector3(a.r - b.r, a.g - b.g, a.b - b.b).length_squared()
			ColorMethod.NEAREST_VALUE:
				if curve != null:
					bright_a = curve.sample(clampf(bright_a, 0.0, 1.0))
				return absf(bright_a - bright_b)
		return 0.0

	func _two_closest(input: Color, p_method: ColorMethod, p_weights: Vector3, curve: Curve) -> Array:
		var best := -1.0
		var second := -1.0
		var best_c := colors[0]
		var second_c := colors[0]
		for c in colors:
			var d := color_distance(input, c, p_method, p_weights, curve)
			if best < 0.0 or d < best:
				second = best
				second_c = best_c
				best = d
				best_c = c
			elif second < 0.0 or d < second:
				second = d
				second_c = c
		return [best_c, second_c]

	func closest_match(input: Color, p_method: ColorMethod, p_weights: Vector3, pattern: PixelDitherPattern, dither_band: int, curve: Curve) -> Color:
		var both := _two_closest(input, p_method, p_weights, curve)
		var closest: Color = both[0]
		var second_closest: Color = both[1]

		# Consistent A/B labels: A is the darker of the two.
		var closest_as_a: bool = (closest.r + closest.g + closest.b) < (second_closest.r + second_closest.g + second_closest.b)
		var c_a: Color = closest if closest_as_a else second_closest
		var c_b: Color = second_closest if closest_as_a else closest

		if pattern == null:
			return closest

		# Find the dither fraction that best reproduces the input.
		var best_score := INF
		var best_fraction := 0.0
		for i in 16:
			var fraction := i / 15.0
			var mixed := c_a.lerp(c_b, fraction)
			var score := absf(color_distance(input, mixed, p_method, p_weights, curve))
			if score < best_score:
				best_fraction = fraction
				best_score = score
		return c_a if pattern.use_color_a(best_fraction, dither_band) else c_b


func bake() -> void:
	if source == null:
		push_error("PixelPalette: assign a source texture before baking.")
		return
	var image := source.get_image()
	if image == null:
		push_error("PixelPalette: could not read source texture.")
		return
	var palette := ColorSwatches.new(image)
	print("PixelPalette: baking LUT from %d unique colors..." % palette.colors.size())

	var pattern: PixelDitherPattern = dither_pattern if use_dither_pattern else null
	var generated := Image.create(RESOLUTION * RESOLUTION, RESOLUTION * DITHER_PATTERN_SIZE, false, Image.FORMAT_RGBA8)
	for dither_band in DITHER_PATTERN_SIZE:
		for b in RESOLUTION:
			var offset := b * RESOLUTION
			for r in RESOLUTION:
				for g in RESOLUTION:
					var u := r + offset
					var v := g + dither_band * RESOLUTION
					var original := Color(r / float(RESOLUTION), g / float(RESOLUTION), b / float(RESOLUTION), 1.0)
					generated.set_pixel(u, v, palette.closest_match(original, method, weights, pattern, dither_band, v_conversion_curve))

	var err := generated.save_png(output_path)
	if err != OK:
		push_error("PixelPalette: failed to save LUT to %s (error %d)." % [output_path, err])
		return
	print("PixelPalette: LUT saved to %s" % output_path)
	# Reimport through the editor filesystem when running as a @tool script.
	if Engine.is_editor_hint() and Engine.has_singleton("EditorInterface"):
		var editor_interface = Engine.get_singleton("EditorInterface")
		if editor_interface != null:
			editor_interface.get_resource_filesystem().scan()
