namespace MuWorldEditor.Render;

public static class Shaders
{
    // Un shader unico para terreno, modelos y ayudas visuales.
    public const string Vertex = @"
#version 120
attribute vec3 aPos;
attribute vec2 aUV;
attribute vec4 aColor;
uniform mat4 uMVP;
uniform mat4 uModel;
varying vec2 vUV;
varying vec4 vColor;
varying float vDepth;
void main() {
    vec4 world = uModel * vec4(aPos, 1.0);
    gl_Position = uMVP * world;
    vUV = aUV;
    vColor = aColor;
    vDepth = gl_Position.w;
}
";

    public const string Fragment = @"
#version 120
uniform sampler2D uTex;
uniform int uUseTexture;
uniform float uAlphaCut;
uniform vec4 uTint;
uniform vec3 uFogColor;
uniform float uFogStart;
uniform float uFogEnd;
uniform float uTime;
uniform int uWater;
uniform vec3 uAmbient;
uniform vec3 uDiffuse;
uniform float uFogDensity;
uniform vec3 uSky;
varying vec2 vUV;
varying vec4 vColor;
varying float vDepth;
void main() {
    vec4 c = vColor * uTint;
    vec2 uv = vUV;
    // el agua se corre y ondula, igual que el muwater.fx del editor original
    if (uWater == 1) {
        uv.x += uTime * 0.1;
        uv.y += sin(uTime * 2.0 + uv.x * 5.0) * 0.02;
    }
    if (uUseTexture == 1) c *= texture2D(uTex, uv);
    if (c.a < uAlphaCut) discard;

    // agua: un brillo que se mueve y un reflejo suave del color del cielo
    if (uWater == 1) {
        float w1 = sin(vUV.x * 9.0 + uTime * 1.7) * cos(vUV.y * 7.0 - uTime * 1.3);
        float w2 = sin((vUV.x + vUV.y) * 13.0 - uTime * 2.3);
        float spark = pow(clamp(w1 * 0.5 + w2 * 0.5, 0.0, 1.0), 6.0);
        c.rgb = mix(c.rgb, uSky, 0.18) + vec3(spark * 0.22);
    }

    // luz de ambiente y difusa de la escena, como en la ventana de config del original
    c.rgb = c.rgb * uDiffuse + uAmbient * 0.5;

    if (uFogEnd > uFogStart) {
        float f = clamp((uFogEnd - vDepth) / (uFogEnd - uFogStart), 0.0, 1.0);
        f = pow(f, max(0.05, uFogDensity));
        c.rgb = mix(uFogColor, c.rgb, f);
    }
    gl_FragColor = c;
}
";
}
