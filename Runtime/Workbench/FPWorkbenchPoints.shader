Shader "Hidden/FuzzPhyte/Workbench/Points"
{
    Properties { _Color ("Point color", Color) = (0,0.7,1,1) _PointSize ("Point pixels", Float) = 4 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color;
            float _PointSize;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.position.xy += (input.uv * 2 - 1) * _PointSize / _ScreenParams.xy * output.position.w;
                output.uv = input.uv * 2 - 1;
                return output;
            }
            float4 frag(Output input) : SV_Target
            {
                clip(1 - dot(input.uv, input.uv));
                return _Color;
            }
            ENDHLSL
        }
    }
}
