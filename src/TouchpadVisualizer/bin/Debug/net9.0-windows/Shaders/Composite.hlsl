// Composite.hlsl — Final compositing pass that combines scene + bloom
// Simple additive blend of the bloom texture onto the scene

cbuffer CompositeBuffer : register(b0)
{
    float BloomIntensity;
    float Exposure;
    float2 _pad;
};

Texture2D SceneTexture : register(t0);
Texture2D BloomTexture : register(t1);
SamplerState LinearSampler : register(s0);

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

float3 ACESFilm(float3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    float3 scene = SceneTexture.Sample(LinearSampler, input.TexCoord).rgb;
    float3 bloom = BloomTexture.Sample(LinearSampler, input.TexCoord).rgb;

    // Additive bloom
    float3 combined = scene + bloom * BloomIntensity;

    // Tone mapping (ACES)
    combined = ACESFilm(combined * Exposure);

    // Gamma correction
    combined = pow(combined, 1.0 / 2.2);

    return float4(combined, 1.0);
}
