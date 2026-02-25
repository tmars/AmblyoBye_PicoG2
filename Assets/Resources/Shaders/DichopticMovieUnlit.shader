Shader "Custom/DichopticMovieUnlit"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "white" {}
        _BlobClipping ("Eye Bias", Range(0,1)) = 0.5
        _BlobScale ("Blob Scale", Float) = 1.0
        _BlobColor ("Blob Color", Color) = (0.5, 0.5, 0.5, 0)
        _BlobOffset ("Blob Offset", Vector) = (0, 0, 0, 0)
        _BlobStretch ("Blob Stretch", Vector) = (1.8, 1, 0, 0)
        _SupressingEyeIndex ("Suppressing Eye", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _BlobClipping;
            float _BlobScale;
            float4 _BlobColor;
            float4 _BlobOffset;
            float4 _BlobStretch;
            float _SupressingEyeIndex;
            float _PicoEyeIndex;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Gradient noise — matches Unity Shader Graph GradientNoiseNode implementation
            float2 gradientNoiseDir(float2 p)
            {
                p = fmod(p, 289.0);
                float x = fmod((34.0 * p.x + 1.0) * p.x, 289.0) + p.y;
                x = fmod((34.0 * x + 1.0) * x, 289.0);
                x = frac(x / 41.0) * 2.0 - 1.0;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            float gradientNoise(float2 p)
            {
                float2 ip = floor(p);
                float2 fp = frac(p);
                float d00 = dot(gradientNoiseDir(ip), fp);
                float d01 = dot(gradientNoiseDir(ip + float2(0, 1)), fp - float2(0, 1));
                float d10 = dot(gradientNoiseDir(ip + float2(1, 0)), fp - float2(1, 0));
                float d11 = dot(gradientNoiseDir(ip + float2(1, 1)), fp - float2(1, 1));
                fp = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
                return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x) + 0.5;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 videoColor = tex2D(_MainTex, i.uv);

                // Scaled UV for noise sampling
                float2 scaledUV = i.uv * _BlobStretch.xy + _BlobOffset.xy * 0.0001;
                float noise = gradientNoise(scaledUV * _BlobScale);

                float eyeIndex = _PicoEyeIndex;
                float noiseMask = step(_BlobClipping, noise);

                // Complementary dichoptic: each eye sees different puzzle pieces
                // Eye 0 (left):  blobs where noise > threshold, video where noise <= threshold
                // Eye 1 (right): video where noise > threshold, blobs where noise <= threshold
                float blobVisible = lerp(noiseMask, 1.0 - noiseMask, eyeIndex);

                return lerp(videoColor, _BlobColor, blobVisible);
            }
            ENDCG
        }
    }

    // _PicoEyeIndex is set per-eye by PicoEyeIndexSetter.cs OnPreRender
}
