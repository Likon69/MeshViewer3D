#version 330 core

// Vertex Shader - Navmesh Renderer
// Qualité Honorbuddy - Simple et efficace

layout (location = 0) in vec3 aPosition;   // Position vertex (Detour coords)
layout (location = 1) in vec3 aColor;      // Couleur par area

out vec3 vColor;
out vec3 vPosition;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
    vColor = aColor;
    vPosition = aPosition;
}
