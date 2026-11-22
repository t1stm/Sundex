#version 330 core

// One pass of the dual Kawase blur, as picom draws it (src/backend/gl/blur.c): a downsample into a
// target half the source's size, or an upsample into one twice its size.

out vec4 color;

in vec2 vUv;

uniform sampler2D uSource;

layout (std140) uniform Draw {
    vec4 u_Params;    // sample offset in source texels, 1 for an upsample
};

void main() {
    vec2 offset = u_Params.x / vec2(textureSize(uSource, 0));

    if (u_Params.y > 0.0) {
        vec4 sum = texture(uSource, vUv + vec2(-1.0, 0.0) * offset);
        sum += texture(uSource, vUv + vec2(-0.5, 0.5) * offset) * 2.0;
        sum += texture(uSource, vUv + vec2(0.0, 1.0) * offset);
        sum += texture(uSource, vUv + vec2(0.5, 0.5) * offset) * 2.0;
        sum += texture(uSource, vUv + vec2(1.0, 0.0) * offset);
        sum += texture(uSource, vUv + vec2(0.5, -0.5) * offset) * 2.0;
        sum += texture(uSource, vUv + vec2(0.0, -1.0) * offset);
        sum += texture(uSource, vUv + vec2(-0.5, -0.5) * offset) * 2.0;
        color = sum / 12.0;
        return;
    }

    vec4 sum = texture(uSource, vUv) * 4.0;
    sum += texture(uSource, vUv - vec2(0.5, 0.5) * offset);
    sum += texture(uSource, vUv + vec2(0.5, 0.5) * offset);
    sum += texture(uSource, vUv + vec2(0.5, -0.5) * offset);
    sum += texture(uSource, vUv - vec2(0.5, -0.5) * offset);
    color = sum / 8.0;
}
