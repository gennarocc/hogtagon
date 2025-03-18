Shader "UI/CRTScanline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ScanlineColor ("Scanline Color", Color) = (0, 1, 0, 1)
        _ScanlineDensity ("Scanline Density", Range(1, 100)) = 20
        _ScanlineWidth ("Scanline Width", Range(0, 1)) = 0.5
        _ScanlineSpeed ("Scanline Speed", Range(0, 10)) = 1
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.1
        _FlickerIntensity ("Flicker Intensity", Range(0, 1)) = 0.05
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "RenderType"="Transparent" 
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        
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
                float4 color : COLOR;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ScanlineColor;
            float _ScanlineDensity;
            float _ScanlineWidth;
            float _ScanlineSpeed;
            float _NoiseIntensity;
            float _FlickerIntensity;
            
            // Random function for noise generation
            float random(float2 st)
            {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the original texture
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                
                // Add noise effect
                float noise = random(i.uv * _Time.y);
                col.rgb += noise * _NoiseIntensity;
                
                // Add flicker effect
                float flicker = 1.0 + sin(_Time.y * 10.0) * _FlickerIntensity;
                col.rgb *= flicker;
                
                // Add scanline effect
                float scanline = frac((i.uv.y + _Time.y * _ScanlineSpeed / 10.0) * _ScanlineDensity);
                scanline = step(_ScanlineWidth, scanline);
                
                // Mix the original color with the scanline effect
                col.rgb = lerp(col.rgb * _ScanlineColor.rgb, col.rgb, scanline);
                
                // Add subtle edge distortion (CRT effect)
                float2 uvCrt = i.uv * 2.0 - 1.0;
                float vignette = 1.0 - dot(uvCrt * 0.5, uvCrt * 0.5);
                col.rgb *= vignette;
                
                return col;
            }
            ENDCG
        }
    }
} 