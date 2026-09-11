#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
uniform mat4 nowui_MatrixMVP;
uniform vec4 _MainTex_ST;
out vec2 vUv;
void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;
}
