//!HOOK POSTKERNEL
vec4 hook() {
    vec4 color = HOOKED_tex(HOOKED_pos);
    color.a = 0.0; // or 0.2 for translucent
    return color;
}
