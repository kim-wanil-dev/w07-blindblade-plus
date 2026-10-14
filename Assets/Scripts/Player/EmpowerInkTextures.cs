using UnityEngine;

/// <summary>
/// 강화 연출에 쓰는 비네트·먹 조각·빛줄기·초승달 곡선 검격 스프라이트를 코드로 한 번 만들어 재사용한다.
/// 모두 흰색 바탕에 투명도만 다른 이미지라 UI Image 색으로 칠한다.
/// </summary>
public static class EmpowerInkTextures
{
    private const int VIGNETTE_SIZE = 128;
    private const int SPLAT_SIZE = 256;
    private const int LINE_WIDTH = 512;
    private const int LINE_HEIGHT = 32;
    private const int ARC_SIZE = 512;
    private const float ARC_HALF_SPAN = 75f;
    private const float ARC_OUTER_RADIUS = 0.96f;
    private const float ARC_MAX_WIDTH = 0.34f;

    private static Sprite _vignette;
    private static Sprite _splat;
    private static Sprite _line;
    private static Sprite _arc;

    public static Sprite Vignette => _vignette != null ? _vignette : _vignette = CreateVignette();
    public static Sprite Splat => _splat != null ? _splat : _splat = CreateSplat();
    public static Sprite Line => _line != null ? _line : _line = CreateLine();
    public static Sprite Arc => _arc != null ? _arc : _arc = CreateArc();
    public static float ArcHalfSpan => ARC_HALF_SPAN;

    /// <summary>
    /// 오른쪽(+x)을 가운데로 위아래 ARC_HALF_SPAN도씩 휘어진 초승달 검격을 만든다.
    /// 가운데가 가장 굵고 양 끝이 뾰족하며, 바깥 날이 가장 밝고 안쪽으로 갈수록 잔상처럼 흐려진다.
    /// 휘는 방향을 따라 결이 남도록 노이즈 줄무늬를 섞은 스프라이트를 반환한다.
    /// </summary>
    private static Sprite CreateArc()
    {
        Color[] pixels = new Color[ARC_SIZE * ARC_SIZE];
        for (int y = 0; y < ARC_SIZE; y++)
        {
            for (int x = 0; x < ARC_SIZE; x++)
            {
                float dx = x / (ARC_SIZE - 1f) * 2f - 1f;
                float dy = y / (ARC_SIZE - 1f) * 2f - 1f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float along = Mathf.InverseLerp(-ARC_HALF_SPAN, ARC_HALF_SPAN, angle);
                if (Mathf.Abs(angle) > ARC_HALF_SPAN)
                {
                    pixels[y * ARC_SIZE + x] = Color.clear;
                    continue;
                }
                // 날의 두께는 가운데에서 가장 두껍고 양 끝에서 0이 된다.
                float width = ARC_MAX_WIDTH * Mathf.Pow(Mathf.Sin(along * Mathf.PI), 0.8f);
                float inner = ARC_OUTER_RADIUS - width;
                float depth = Mathf.InverseLerp(inner, ARC_OUTER_RADIUS, radius);
                float edge = 1f - Mathf.SmoothStep(ARC_OUTER_RADIUS - 0.012f, ARC_OUTER_RADIUS, radius);
                float trail = radius < inner ? 0f : Mathf.Pow(depth, 1.8f);
                float streak = Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(radius * 60f, along * 3f));
                float alpha = Mathf.Clamp01(trail * edge * Mathf.Lerp(streak, 1f, depth * depth));
                pixels[y * ARC_SIZE + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        return CreateSprite(ARC_SIZE, ARC_SIZE, pixels);
    }

    /// <summary>가운데는 비고 가장자리로 갈수록 진해지는 화면 비네트 스프라이트를 만들어 반환한다.</summary>
    private static Sprite CreateVignette()
    {
        Color[] pixels = new Color[VIGNETTE_SIZE * VIGNETTE_SIZE];
        for (int y = 0; y < VIGNETTE_SIZE; y++)
            for (int x = 0; x < VIGNETTE_SIZE; x++)
                pixels[y * VIGNETTE_SIZE + x] = new Color(1f, 1f, 1f,
                    Mathf.SmoothStep(0.45f, 1.3f, Radius(x, y, VIGNETTE_SIZE)));
        return CreateSprite(VIGNETTE_SIZE, VIGNETTE_SIZE, pixels);
    }

    /// <summary>
    /// 가운데 먹 덩어리와 각도마다 다른 길이로 튀어 나간 가시, 주변 먹 방울로 이루어진 먹 튐 스프라이트를 만든다.
    /// 노이즈 위치는 고정이라 매번 같은 모양을 반환한다.
    /// </summary>
    private static Sprite CreateSplat()
    {
        Color[] pixels = new Color[SPLAT_SIZE * SPLAT_SIZE];
        for (int y = 0; y < SPLAT_SIZE; y++)
        {
            for (int x = 0; x < SPLAT_SIZE; x++)
            {
                float dx = x / (SPLAT_SIZE - 1f) * 2f - 1f;
                float dy = y / (SPLAT_SIZE - 1f) * 2f - 1f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                float spike = Mathf.Pow(Mathf.PerlinNoise(angle * 2.2f + 20f, 1.3f), 3f);
                float reach = 0.32f + spike * 0.65f;
                float body = 1f - Mathf.SmoothStep(reach - 0.04f, reach, radius);
                float drops = Mathf.SmoothStep(0.78f, 0.82f, Mathf.PerlinNoise(x * 0.09f + 40f, y * 0.09f + 40f))
                    * (1f - Mathf.SmoothStep(0.85f, 0.98f, radius));
                pixels[y * SPLAT_SIZE + x] = new Color(1f, 1f, 1f, Mathf.Max(body, drops));
            }
        }
        return CreateSprite(SPLAT_SIZE, SPLAT_SIZE, pixels);
    }

    /// <summary>가운데가 가장 밝고 위아래로 번지며 양 끝이 가늘게 사라지는 빛줄기 스프라이트를 만들어 반환한다.</summary>
    private static Sprite CreateLine()
    {
        Color[] pixels = new Color[LINE_WIDTH * LINE_HEIGHT];
        for (int x = 0; x < LINE_WIDTH; x++)
        {
            float u = Mathf.Abs(x / (LINE_WIDTH - 1f) * 2f - 1f);
            float taper = 1f - Mathf.SmoothStep(0.7f, 1f, u);
            for (int y = 0; y < LINE_HEIGHT; y++)
            {
                float v = Mathf.Abs(y / (LINE_HEIGHT - 1f) * 2f - 1f);
                float core = 1f - Mathf.SmoothStep(0f, 0.2f, v);
                float glow = Mathf.Pow(1f - v, 4f) * 0.5f;
                pixels[y * LINE_WIDTH + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + glow) * taper);
            }
        }
        return CreateSprite(LINE_WIDTH, LINE_HEIGHT, pixels);
    }

    /// <summary>size 정사각형 이미지에서 픽셀 x·y의 중심 기준 정규화 거리를 반환한다. 가장자리 중앙이 1이다.</summary>
    private static float Radius(int x, int y, int size)
    {
        float dx = x / (size - 1f) * 2f - 1f;
        float dy = y / (size - 1f) * 2f - 1f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>width·height 텍스처에 pixels를 적용하고 전체 영역을 쓰는 스프라이트를 만들어 반환한다.</summary>
    private static Sprite CreateSprite(int width, int height, Color[] pixels)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.DontSave;
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
