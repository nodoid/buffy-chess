// LitTint.fx — the chapter 6 custom shader, authored once in HLSL.
//
// MonoGame's MGFX cross-compiler turns this ONE source file into backend
// bytecode for DirectX (HLSL), OpenGL/OpenGL ES (GLSL) and Metal (MSL) at
// content-build time — the same .mgfxo bytes ship inside every platform
// package from chapter 12. The desktop shell in this chapter draws with
// BasicEffect (no content pipeline); this shader is what the content
// pipeline build compiles for the shipped product look. See §6.4.

float4x4 World;
float4x4 View;
float4x4 Projection;

float3 TintColor = float3(1, 1, 1);          // chapter 5's skin colours ride here
float3 LightDirection = float3(-0.5, 1.0, -0.35); // normalized in the app
float3 LightColor = float3(1.0, 0.93, 0.82);
float3 RimColor = float3(0.35, 0.42, 0.55);
float  RimPower = 2.0;

struct VS_INPUT
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float3 Normal   : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
};

void TintedVertexShader(in VS_INPUT input, out VS_OUTPUT output)
{
    float4 world = mul(input.Position, World);
    output.WorldPos = world.xyz;
    output.Normal = normalize(mul(input.Normal, (float3x3)World));
    output.Position = mul(world, mul(View, Projection));
}

// One classic diffuse term plus a view-independent rim fake: rim lighting
// needs the *view* vector to be physically correct, but a fixed rim direction
// is backend-proof, branch-free, and reads as StudioLighting at board scale.
float4 TintedPixelShader(VS_OUTPUT input) : SV_TARGET
{
    float3 n = normalize(input.Normal);
    float ndl = saturate(dot(n, LightDirection));

    float3 lit = TintColor * LightColor * ndl;
    lit += TintColor * RimColor * pow(1.0 - saturate(dot(n, float3(0, 1, 0))), RimPower);
    lit += TintColor * float3(0.28, 0.28, 0.32); // the ambient floor from §6.4

    return float4(lit, 1);
}

technique TintedLit
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL TintedVertexShader();
        PixelShader = compile PS_SHADERMODEL TintedPixelShader();
    }
}
