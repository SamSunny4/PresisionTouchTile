// Particle.hlsl — GPU-accelerated particle rendering with glow and additive blending
// Renders particles as instanced quads with soft circular falloff

cbuffer FrameBuffer : register(b0)
{
    float4x4 ViewProjection;
    float Time;
    float GlowIntensity;
    float2 Padding;
};

struct VS_INPUT
{
    // Per-vertex data (unit quad)
    float2 QuadPos : POSITION;

    // Per-instance data
    float2 Center : INST_CENTER;
    float2 Size : INST_SIZE;
    float4 Color : INST_COLOR;
    float Rotation : INST_ROTATION;
    float Glow : INST_GLOW;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float Glow : TEXCOORD1;
};

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;

    // Apply rotation
    float cosR = cos(input.Rotation);
    float sinR = sin(input.Rotation);
    float2 rotated;
    rotated.x = input.QuadPos.x * cosR - input.QuadPos.y * sinR;
    rotated.y = input.QuadPos.x * sinR + input.QuadPos.y * cosR;

    // Scale and translate
    float2 worldPos = input.Center + rotated * input.Size;

    output.Position = mul(float4(worldPos, 0.0, 1.0), ViewProjection);
    output.TexCoord = input.QuadPos * 0.5 + 0.5;
    output.Color = input.Color;
    output.Glow = input.Glow;
    return output;
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    // Soft circular particle
    float2 center = input.TexCoord - 0.5;
    float dist = length(center) * 2.0;

    // Smooth falloff
    float alpha = 1.0 - smoothstep(0.0, 1.0, dist);
    alpha = pow(alpha, 1.5);

    // Inner glow (brighter center)
    float innerGlow = exp(-dist * dist * 8.0) * input.Glow * GlowIntensity;

    float4 color = input.Color;
    color.rgb += innerGlow * color.rgb;
    color.a *= alpha;

    // Energy effect — slight color shift based on glow
    color.rgb += float3(0.1, 0.05, 0.2) * innerGlow * 0.5;

    return color;
}
