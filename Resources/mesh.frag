#version 330 core

// Fragment Shader - Navmesh Renderer
// Qualité Honorbuddy - Lighting simple + fog + alpha

in vec3 vColor;
in vec3 vPosition;

out vec4 FragColor;

uniform vec3 uLightDir;
uniform float uAmbient;
uniform float uAlpha;
uniform bool uEnableLighting;
uniform bool uEnableFog;
uniform vec3 uCameraPos;

void main()
{
    vec3 color = vColor;
    float alpha = 1.0;
    
    if (uEnableLighting)
    {
        // Simple directional lighting (fake normal from gradient)
        vec3 dx = dFdx(vPosition);
        vec3 dy = dFdy(vPosition);
        vec3 normal = normalize(cross(dx, dy));
        
        vec3 lightDir = normalize(vec3(0.3, 0.7, 0.5));
        float diffuse = max(dot(normal, lightDir), 0.0);
        float ambient = 0.4;
        float lighting = ambient + (1.0 - ambient) * diffuse;
        
        color *= lighting;
    }
    
    // Alpha override (pour transparence des blackspots/volumes)
    if (uAlpha > 0.0 && uAlpha < 1.0)
    {
        alpha = uAlpha;
    }
    
    if (uEnableFog)
    {
        // Distance fog (bleu-gris comme HB)
        float dist = length(vPosition - uCameraPos);
        float fogFactor = exp(-dist * 0.0002);
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        vec3 fogColor = vec3(0.39, 0.47, 0.55);
        color = mix(fogColor, color, fogFactor);
    }
    
    FragColor = vec4(color, alpha);
}


