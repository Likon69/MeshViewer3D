#version 330 core

// Vertex Shader - Instanced WMO/M2 renderer
// Same math as mesh.vert but with per-instance model matrix instead of uniform uModel.
// Loads 4 vec4 attributes (16 floats total) per instance via GL.VertexAttribDivisor.

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aColor;

layout (location = 2) in vec4 aModelRow0;
layout (location = 3) in vec4 aModelRow1;
layout (location = 4) in vec4 aModelRow2;
layout (location = 5) in vec4 aModelRow3;

out vec3 vColor;
out vec3 vPosition;

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    mat4 uModel = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    vColor = aColor;
    vPosition = aPosition;
}