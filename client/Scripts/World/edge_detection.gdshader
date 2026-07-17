shader_type canvas_item;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform vec4 edge_color : source_color = vec4(0.4, 0.4,0.4,1);
uniform vec2 resolution = vec2(800.0,600.0);

void fragment(){
    float width = resolution.x;
    float height = resolution.y;
	float w = 1.0 / width;
	float h = 1.0 / height;

	vec4 n0 = texture(screen_texture, SCREEN_UV + vec2( -w, -h));
	vec4 n1 = texture(screen_texture, SCREEN_UV + vec2(0.0, -h));
	vec4 n2 = texture(screen_texture, SCREEN_UV + vec2(  w, -h));
	vec4 n3 = texture(screen_texture, SCREEN_UV + vec2( -w, 0.0));
	vec4 n4 = texture(screen_texture, SCREEN_UV);
	vec4 n5 = texture(screen_texture, SCREEN_UV + vec2(  w, 0.0));
	vec4 n6 = texture(screen_texture, SCREEN_UV + vec2( -w, h));
	vec4 n7 = texture(screen_texture, SCREEN_UV + vec2(0.0, h));
	vec4 n8 = texture(screen_texture, SCREEN_UV + vec2(  w, h));



	vec4 sobel_edge_h = n2 + (2.0*n5) + n8 - (n0 + (2.0*n3) + n6);
  	vec4 sobel_edge_v = n0 + (2.0*n1) + n2 - (n6 + (2.0*n7) + n8);
	vec4 sobel = sqrt((sobel_edge_h * sobel_edge_h) + (sobel_edge_v * sobel_edge_v));
    float alpha = sobel.r;
    alpha += sobel.g;
    alpha +=  sobel.b;
    alpha /= 3.0;
	COLOR = vec4( edge_color.rgb, alpha );
}
