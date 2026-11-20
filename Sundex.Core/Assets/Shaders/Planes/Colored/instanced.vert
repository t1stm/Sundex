#version 330 core

layout (std140) uniform View {
    mat4 ViewProjection;
};

layout (location = 0) in vec3 aPosition;
layout (location = 1) in mat4 aModel;
layout (location = 5) in vec4 aRGBA;

out vec4 vRGBA;


void main() {
    gl_Position = ViewProjection * aModel * vec4(aPosition, 1.0);
    vRGBA = aRGBA;
}
