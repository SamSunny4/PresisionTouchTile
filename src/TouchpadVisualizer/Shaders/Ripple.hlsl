// Ripple.hlsl — Expanding ring shockwave effect triggered on touch down
// Each ripple is an expanding, fading ring with glow

cbuffer RippleBuffer : register(b0)
{
    float4x4 ViewProjection;
    float Time;
    float GlowIntensity;
    float2 _pad;
};

struct RippleInstance
{
    float2 Center;
    float StartTime;
    float Intensity;
    float4 Color;
};

cbuffer RippleDataBuffer : register(b1)
{
    RippleInstance Ripples[32];
    int RippleCount;
    float3 _pad2;
};

struct VS_INPUT
{
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    float2 uv = input.TexCoord;
    float4 result = float4(0, 0, 0, 0);

    for (int i = 0; i < RippleCount; i++)
    {
        float age = Time - Ripples[i].StartTime;
        if (age < 0.0 || age > 2.0)
            continue;

        float2 delta = uv - Ripples[i].Center;
        float dist = length(delta);

        // Expanding ring
        float ringRadius = age * 0.3;
        float ringWidth = 0.008 + age * 0.004;

        float ring = smoothstep(ringWidth, 0.0, abs(dist - ringRadius));

        // Fade out over time
        float fade = 1.0 - smoothstep(0.0, 2.0, age);
        fade = pow(fade, 2.0);

        // Inner shockwave
        float shockwave = exp(-pow((dist - ringRadius * 0.5) * 20.0, 2.0));
        shockwave *= smoothstep(1.0, 0.0, age) * 0.5;

        float4 rippleColor = Ripples[i].Color;
        rippleColor.rgb *= (ring + shockwave) * fade * Ripples[i].Intensity * GlowIntensity;
        rippleColor.a = (ring + shockwave) * fade * Ripples[i].Intensity;

        result += rippleColor;
    }

    return result;
}
