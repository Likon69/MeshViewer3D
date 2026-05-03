#version 330 core

// Fragment Shader — ADT Terrain Renderer
// Samples BLP texture when available, falls back to sandy colour.

in vec2  vTexCoord;
in vec3  vPosition;

out vec4 FragColor;

uniform sampler2D uTexture;
uniform bool      uHasTexture;
uniform float     uAlpha;
uniform bool      uEnableLighting;

void main()
{
    vec3 color;

    if (uHasTexture)
        color = texture(uTexture, vTexCoord).rgb;
    else
        color = vec3(0.60, 0.55, 0.40);   // sandy fallback

    if (uEnableLighting)
    {
        vec3 dx     = dFdx(vPosition);
        vec3 dy     = dFdy(vPosition);
        vec3 normal = normalize(cross(dx, dy));

        vec3  lightDir = normalize(vec3(0.3, 0.7, 0.5));
        float diffuse  = max(dot(normal, lightDir), 0.0);
        float ambient  = 0.5;
        color *= ambient + (1.0 - ambient) * diffuse;
    }

    FragColor = vec4(color, uAlpha);
}
