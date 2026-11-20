#version 330 core

layout (std140) uniform View {
    mat4 ViewProjection;
};

layout (location = 0) in vec3 aPosition;

out vec2 vLocalPos;

layout (std140) uniform Draw {
    mat4 u_Model;

    vec4 u_Color;
    vec4 u_ScaleAndBorderPx;
};

void main() {
    vLocalPos = (aPosition.xy - 0.5) * u_ScaleAndBorderPx.xy;
    vec4 final_coords = ViewProjection * u_Model * vec4(aPosition, 1.0);
    gl_Position = final_coords;
}
