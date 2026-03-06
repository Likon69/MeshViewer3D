#version 330 core

// Fragment Shader - Per-vertex colored lines (Jump Links / Custom OffMesh Connections)

in vec3 vColor;

out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
