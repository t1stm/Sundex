#version 330 core

layout (std140) uniform View {
    mat4 ViewProjection;
};

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aUV;

out vec2 vLocalPos;
out vec2 vUV;

layout (std140) uniform Draw {
    mat4 u_Model;
    vec4 u_ScaleAndBorderPx;
    vec4 u_Color;
    vec4 u_UvRect;
};

void main() {
    vLocalPos = (aPosition.xy - 0.5) * u_ScaleAndBorderPx.xy;
    vUV = u_UvRect.xy + aUV * u_UvRect.zw;
    vec4 final_coords = ViewProjection * u_Model * vec4(aPosition, 1.0);
    gl_Position = final_coords;
}
