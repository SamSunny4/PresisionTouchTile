// Trail.hlsl — Fading motion trail rendered as a glowing line strip
// Trail points are stored as a sequence of positions with age data

cbuffer TrailBuffer : register(b0)
{
    float4x4 ViewProjection;
    float GlowIntensity;
    float TrailWidth;
    float2 _pad;
};

struct VS_INPUT
{
    float2 Position : POSITION;
    float2 Normal : NORMAL;    // perpendicular direction for width
    float4 Color : COLOR0;
    float Age : TEXCOORD0;     // 0 = newest, 1 = oldest
    float Side : TEXCOORD1;    // -1 or 1 (left/right of center line)
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float Age : TEXCOORD0;
    float Side : TEXCOORD1;
};

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;

    // Expand the center line into a ribbon using the normal and side
    float width = TrailWidth * (1.0 - input.Age * 0.8); // Taper toward tail
    float2 expanded = input.Position + input.Normal * input.Side * width;

    output.Position = mul(float4(expanded, 0.0, 1.0), ViewProjection);
    output.Color = input.Color;
    output.Age = input.Age;
    output.Side = input.Side;
    return output;
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    float4 color = input.Color;

    // Fade based on age (older = more transparent)
    float ageFade = 1.0 - pow(input.Age, 1.5);

    // Soft edge falloff (fade at ribbon edges)
    float edgeDist = abs(input.Side);
    float edgeFade = 1.0 - smoothstep(0.3, 1.0, edgeDist);

    // Center glow
    float centerGlow = exp(-edgeDist * edgeDist * 8.0) * GlowIntensity * 0.5;

    color.rgb += centerGlow * color.rgb;
    color.a *= ageFade * edgeFade;

    return color;
}
