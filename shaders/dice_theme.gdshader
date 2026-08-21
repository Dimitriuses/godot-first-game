shader_type canvas_item;

// Recolours a die without re-rendering it.
//
// The artwork is toon-shaded from a fixed six-colour palette — four body bands
// (#F1F0F7 #D6DCEA #BDBECF #929AAB) plus one near-black (#121212) used for BOTH the
// glyphs and the outline. Motion blur and antialiasing smear those into some 9,500
// distinct colours per sheet, so swapping exact values is hopeless; what survives the
// blur is luminance, and every mode here is a function of it.
//
// Mode 3 is the only one that can tell a glyph from the outline, and it does it by
// geometry rather than colour: an outline pixel has transparency within a few pixels of
// it, a pip does not.

// 0 untouched, 1 multiply, 2 body ramp, 3 body ramp + separate glyphs, 4 full ramp.
uniform int mode : hint_range(0, 4) = 2;

uniform vec4 tint : source_color = vec4(1.0);

// The theme, as a gradient read left (deepest shade) to right (highlight). A gradient
// rather than four colours because the blur lands everywhere between the bands, and a
// texture interpolates for free.
uniform sampler2D ramp : repeat_disable, filter_linear;

uniform vec4 glyph_color : source_color = vec4(0.07, 0.07, 0.07, 1.0);

// Where the body sits on the luminance scale. Measured off the sheets: the darkest
// band lands at 0.60 and the lit band at 0.94, with the glyphs down at 0.06-0.16.
uniform float body_low = 0.52;
uniform float body_high = 0.96;

// Below this is glyph or outline; the width is the crossfade, which the blur needs.
uniform float glyph_cut = 0.30;
uniform float glyph_soft = 0.14;

// How far to look for transparency when deciding whether a dark pixel is the outline
// rather than a pip, in texels, and how much alpha counts as "edge".
//
// 2.5 is measured, not guessed: composite.py dilates the silhouette with a 9px gaussian
// at 512, which is a little over 2px of outline once the sheet is down at 128. Below 2px
// the rim itself takes the glyph colour and the die loses its edge; above 3px the numbers
// on a d20 — which sit within a few pixels of the silhouette — start being eaten.
uniform float outline_reach = 2.5;
uniform float outline_alpha = 0.6;

// 1 while the die is playing `idle1`, the fast spin. That one clip is rendered through a
// sweeping rainbow (tools/dice-render/render.py, set_palette_gradient) rather than in the
// grey palette everything else uses, and it needs two corrections that would be wrong
// anywhere else. Set from Dice.cs, because which clip is playing is not something a
// fragment can work out: the d6's red pip is *more* saturated than the rainbow is, so
// deciding per pixel greys out the pip and gets the clip wrong at the same time.
uniform float rainbow = 0.0;

float luma(vec3 c) {
	return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	vec3 rgb = src.rgb;

	// No early return: Godot forbids `return` in an entry function.
	if (src.a >= 0.004 && mode != 0) {
		// Under the rainbow, read the shade from the brightest channel rather than from
		// luminance. The rainbow is a tint multiplied over the palette bands and the tint
		// is built with HSV value 1.0, so its brightest channel is 1.0 — which makes the
		// pixel's brightest channel the band's own shade with the hue divided back out.
		// Luminance instead makes a themed die pulse as the hue sweeps: the yellow frames
		// read bright and the blue ones dark, for no reason the player can see.
		float maxc = max(src.r, max(src.g, src.b));
		float l = mix(luma(src.rgb), maxc, rainbow);
		// 0 where the pixel is glyph or outline, 1 where it is body.
		float body = smoothstep(glyph_cut, glyph_cut + glyph_soft, l);
		// Where the body sits between its own darkest shade and its highlight.
		float shade = clamp((l - body_low) / max(0.0001, body_high - body_low), 0.0, 1.0);
		vec3 themed = texture(ramp, vec2(shade, 0.5)).rgb;

		if (mode == 1) {
			rgb = src.rgb * tint.rgb;
		} else if (mode == 2) {
			// Body only. Glyphs and outline are left exactly as rendered, which keeps
			// the die readable whatever colour the body becomes.
			//
			// The other rainbow correction: drain the colour out of what is kept.
			// Leaving dark pixels alone is invisible when they are near-black, which they
			// are on every clip but idle1 — there they are dark *rainbow*, and a crimson
			// die spun up with a gold rim round it is the confusing part.
			vec3 kept = mix(src.rgb, vec3(luma(src.rgb)), rainbow);
			rgb = mix(kept, themed, body);
		} else if (mode == 3) {
			// Glyphs get their own colour, but only where they are not the rim —
			// otherwise a pale glyph colour eats the silhouette and the die stops
			// having an edge.
			// Only fully opaque pixels can be a glyph. The drop shadow is dark too,
			// but it is semi-transparent, and without this it turned the glyph colour
			// and left a pale smear under the die.
			float solid = smoothstep(0.90, 0.99, src.a);

			// Is there transparency within outline_reach of here? If so this is the rim
			// rather than something drawn inside the die.
			//
			// Inlined rather than a tidy helper because Godot will not pass a built-in
			// sampler into a user function: it compiles, renders correctly and logs
			// "!actions.custom_samplers.has(...)" once per call every time the shader
			// loads. A construct the engine complains about is not one to ship to a
			// second backend.
			//
			// textureLod, not texture: these taps sit inside a branch on the pixel's own
			// alpha, which is non-uniform control flow, and sampling with implicit
			// derivatives there is undefined in GLSL ES 3.00 — the dialect a WebGL2
			// export compiles to.
			vec2 r = outline_reach / vec2(textureSize(TEXTURE, 0));
			vec2 dg = r * 0.7071;
			float lowest = 1.0;
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2( r.x, 0.0), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2(-r.x, 0.0), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2(0.0,  r.y), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2(0.0, -r.y), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2( dg.x,  dg.y), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2( dg.x, -dg.y), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2(-dg.x,  dg.y), 0.0).a);
			lowest = min(lowest, textureLod(TEXTURE, UV + vec2(-dg.x, -dg.y), 0.0).a);
			float rim = 1.0 - smoothstep(outline_alpha - 0.25, outline_alpha, lowest);

			float is_glyph = (1.0 - rim) * solid;
			vec3 dark = mix(src.rgb, glyph_color.rgb, is_glyph);
			rgb = mix(dark, themed, body);
		} else {
			// Everything through the ramp, glyphs and outline included: one-colour print.
			rgb = texture(ramp, vec2(clamp(l / max(0.0001, body_high), 0.0, 1.0), 0.5)).rgb;
		}
	}

	COLOR = vec4(rgb, src.a);
}
