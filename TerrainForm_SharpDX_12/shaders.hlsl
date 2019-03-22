cbuffer cbPerRbject : register(b0)
{
	float4x4 WorldViewProj;
};
void VS(float3 iPosL : POSITION,
	float2 iTex : TEXCOORD,
	out float4 oPosH : SV_POSITION,
	out float2 oTex : TEXCOORD)
{
	oPosH = mul(float4(iPosL, 1.0f), WorldViewProj);
	oTex = iTex;
}