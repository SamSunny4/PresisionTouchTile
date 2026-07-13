// Bloom.hlsl — Post-processing bloom with bright pass extraction and Gaussian blur
// Used in two passes: horizontal blur, then vertical blur

cbuffer BloomBuffer : register(b0)
{
    float2 TexelSize;   // 1.0 / texture dimensions
    float Threshold;
    float Intensity;
    int IsHorizontal;   // 1 = horizontal pass, 0 = vertical pass
    int IsBrightPass;   // 1 = extract bright pixels, 0 = blur pass
    float2 _padding;
};

Texture2D SourceTexture : register(t0);
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

// 9-tap Gaussian kernel weights
static const float Weights[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };

float4 PSBrightPass(PS_INPUT input) : SV_TARGET
{
    float4 color = SourceTexture.Sample(LinearSampler, input.TexCoord);

    // Extract bright pixels based on luminance
    float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    float brightness = max(0.0, luminance - Threshold);
    float contribution = brightness / (brightness + 1.0); // Soft knee

    return float4(color.rgb * contribution, 1.0);
}

float4 PSBlur(PS_INPUT input) : SV_TARGET
{
    float2 uv = input.TexCoord;
    float3 result = SourceTexture.Sample(LinearSampler, uv).rgb * Weights[0];

    float2 blurDir = IsHorizontal ? float2(1.0, 0.0) : float2(0.0, 1.0);

    for (int i = 1; i < 5; i++)
    {
        float2 offset = blurDir * TexelSize * (float)i * 1.5;
        result += SourceTexture.Sample(LinearSampler, uv + offset).rgb * Weights[i];
        result += SourceTexture.Sample(LinearSampler, uv - offset).rgb * Weights[i];
    }

    return float4(result, 1.0);
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    if (IsBrightPass)
        return PSBrightPass(input);
    else
        return PSBlur(input);
}
