// Background.hlsl — Animated gradient with breathing effect and floating particles
// Renders a full-screen quad with morphing gradient using the specified color palette

cbuffer TimeBuffer : register(b0)
{
    float Time;
    float Speed;
    float2 Resolution;
};

cbuffer ColorBuffer : register(b1)
{
    float4 Colors[5]; // Gradient colors
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

// Simplex-like noise for organic movement
float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f); // Smoothstep

    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < 5; i++)
    {
        value += amplitude * noise(p * frequency);
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

float4 PSMain(PS_INPUT input) : SV_TARGET
{
    float2 uv = input.TexCoord;
    float t = Time * Speed * 0.15;

    // Create morphing gradient coordinates
    float2 distortedUV = uv;
    distortedUV.x += sin(t * 0.7 + uv.y * 3.0) * 0.05;
    distortedUV.y += cos(t * 0.5 + uv.x * 2.5) * 0.04;

    // FBM-based organic distortion
    float n = fbm(distortedUV * 3.0 + t * 0.3);
    float n2 = fbm(distortedUV * 2.0 - t * 0.2 + 5.0);

    // Blend between gradient colors based on position and noise
    float gradientPos = distortedUV.y + n * 0.3 + n2 * 0.2;
    gradientPos = saturate(gradientPos);

    // 5-stop gradient
    float4 color;
    float segment = gradientPos * 4.0;

    if (segment < 1.0)
        color = lerp(Colors[0], Colors[1], segment);
    else if (segment < 2.0)
        color = lerp(Colors[1], Colors[2], segment - 1.0);
    else if (segment < 3.0)
        color = lerp(Colors[2], Colors[3], segment - 2.0);
    else
        color = lerp(Colors[3], Colors[4], segment - 3.0);

    // Breathing brightness
    float breath = 0.85 + 0.15 * sin(t * 1.2);
    color.rgb *= breath;

    // Subtle vignette
    float2 vc = (uv - 0.5) * 2.0;
    float vignette = 1.0 - dot(vc, vc) * 0.3;
    color.rgb *= vignette;

    // Floating particles
    float particles = 0.0;
    for (int i = 0; i < 8; i++)
    {
        float fi = (float)i;
        float2 pos = float2(
            frac(sin(fi * 127.1 + t * 0.1) * 43758.5453),
            frac(cos(fi * 311.7 + t * 0.08) * 43758.5453)
        );
        float dist = length(uv - pos);
        float size = 0.001 + 0.002 * frac(sin(fi * 73.1) * 43758.0);
        particles += smoothstep(size * 2.0, 0.0, dist) * 0.03;
    }
    color.rgb += particles * float3(0.6, 0.5, 1.0);

    color.a = 1.0;
    return color;
}
