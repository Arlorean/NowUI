#version 330 core
uniform sampler2D _MainTex;
in vec2 vUv;
out vec4 fragColor;
void main() { fragColor = texture(_MainTex, vUv); }
