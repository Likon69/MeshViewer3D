#version 330 core

// Vertex Shader — ADT Terrain Renderer
// Vertex format: [position(3), texcoord(2)] = 5 floats

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;
out vec3 vPosition;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    vTexCoord   = aTexCoord;
    vPosition   = aPosition;
}
