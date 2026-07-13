// Touchpad.hlsl — Glass/frosted touchpad surface with metallic border and reflections
// Renders the central touchpad rectangle with premium material effects

cbuffer TouchpadBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float Time;
    float GlowIntensity;
    float2 TouchpadSize; // width, height in NDC
};

cbuffer TouchBuffer : register(b1)
{
    float4 TouchPositions[10]; // xy = position, z = intensity, w = active
    int ActiveTouchCount;
    float3 _pad;
};

struct VS_INPUT
{
    float3 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
    float3 Normal : NORMAL;
};

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
    float3 Normal : TEXCOORD2;
};

PS_INPUT VSMain(VS_INPUT input)
{
    PS_INPUT output;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.TexCoord = input.TexCoord;
    output.WorldPos = mul(float4(input.Position, 1.0), World).xyz;
    output.Normal = normalize(mul(float4(input.Normal, 0.0), World).xyz);
    return output;
}

float roundedRectSDF(float2 p, float2 b, float r)
{
    float2 d = abs(p) - b + r;
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    float2 uv = input.TexCoord;
    float2 centered = uv * 2.0 - 1.0; // -1 to 1

    // Rounded rectangle mask
    float cornerRadius = 0.08;
    float dist = roundedRectSDF(centered, float2(0.92, 0.92), cornerRadius);
    float mask = 1.0 - smoothstep(-0.01, 0.01, dist);

    if (mask < 0.01)
        discard;

    // Base glass color — dark frosted surface
    float4 baseColor = float4(0.08, 0.08, 0.15, 0.75);

    // Frosted glass noise
    float frost = frac(sin(dot(uv * 200.0, float2(12.9898, 78.233))) * 43758.5453);
    frost = frost * 0.03;
    baseColor.rgb += frost;

    // Subtle grid pattern (touchpad texture)
    float gridX = smoothstep(0.98, 1.0, abs(sin(uv.x * 80.0)));
    float gridY = smoothstep(0.98, 1.0, abs(sin(uv.y * 60.0)));
    float grid = max(gridX, gridY) * 0.02;
    baseColor.rgb += grid;

    // Reflection gradient — simulate overhead light
    float reflection = pow(1.0 - abs(centered.y - 0.3), 4.0) * 0.08;
    reflection *= smoothstep(0.0, 0.5, 1.0 - abs(centered.x));
    baseColor.rgb += reflection;

    // Moving reflection highlight
    float moveRefl = sin(Time * 0.5 + centered.x * 2.0) * 0.5 + 0.5;
    moveRefl *= pow(1.0 - abs(centered.y + 0.2), 6.0) * 0.04;
    baseColor.rgb += moveRefl;

    // Metallic border glow
    float borderDist = abs(dist);
    float borderGlow = smoothstep(0.05, 0.0, borderDist) * 0.4;
    float3 borderColor = float3(0.4, 0.35, 0.55); // Subtle metallic purple
    baseColor.rgb += borderColor * borderGlow;

    // Ambient edge glow
    float edgeGlow = smoothstep(0.02, 0.0, borderDist) * 0.6;
    baseColor.rgb += float3(0.3, 0.2, 0.8) * edgeGlow * GlowIntensity;

    // Touch point highlights — glow where fingers are touching
    for (int i = 0; i < ActiveTouchCount; i++)
    {
        if (TouchPositions[i].w > 0.0)
        {
            float2 touchUV = TouchPositions[i].xy;
            float touchIntensity = TouchPositions[i].z;
            float touchDist = length(uv - touchUV);

            // Warm glow at touch point
            float touchGlow = exp(-touchDist * touchDist * 80.0) * touchIntensity;
            baseColor.rgb += float3(0.5, 0.3, 1.0) * touchGlow * GlowIntensity * 0.5;

            // Subtle ripple ring
            float ripple = abs(sin(touchDist * 40.0 - Time * 3.0));
            ripple *= exp(-touchDist * 8.0) * touchIntensity * 0.1;
            baseColor.rgb += float3(0.4, 0.3, 0.9) * ripple;
        }
    }

    baseColor.a *= mask;
    return baseColor;
}
