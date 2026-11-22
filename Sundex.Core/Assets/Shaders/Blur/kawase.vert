#version 330 core

// Covers the whole pass target, for one dual Kawase pass.

layout (location = 0) in vec3 aPosition;

out vec2 vUv;

void main() {
    vUv = aPosition.xy;
    gl_Position = vec4(aPosition.xy * 2.0 - 1.0, 0.0, 1.0);
}
