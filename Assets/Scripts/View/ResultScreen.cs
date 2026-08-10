using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 도착 뒤 성적을 보여주는 결과 화면. 별이 하나씩 튀어나오며 등급을 알린다.
    ///
    /// 프로젝트의 다른 화면과 같이 IMGUI 로 그린다. 이 게임에는 uGUI Canvas 가 한 곳도 없고
    /// 메인 메뉴도 같은 방식이라, 여기서만 다른 UI 체계를 들이지 않는다.
    ///
    /// 별 모양은 텍스처를 코드로 만들어 쓴다. 기본 폰트에 ★ 글자가 있다는 보장이 없고,
    /// 크기를 매 프레임 바꾸는 연출은 글자보다 텍스처 쪽이 다루기 쉽기 때문이다.
    /// 흰 별 하나를 만들어 두고 색만 갈아 끼워 받은 별과 못 받은 별을 함께 그린다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ResultScreen : MonoBehaviour
    {
        [SerializeField] private GameFlow flow;
        [SerializeField] private CargoTracker tracker;
        [SerializeField] private Font titleFont;
        [SerializeField] private Font bodyFont;

        /// <summary>별 하나가 다 커지는 데 걸리는 시간.</summary>
        private const float StarGrowSeconds = 0.28f;

        /// <summary>다음 별이 나오기까지의 간격. 셋이 한꺼번에 나오면 몇 개인지 셀 수가 없다.</summary>
        private const float StarStaggerSeconds = 0.18f;

        /// <summary>커지다가 잠깐 넘어서는 배율. 이 오버슛이 "통통" 튀는 느낌을 만든다.</summary>
        private const float OvershootScale = 1.3f;

        /// <summary>오버슛에 닿는 시점(0~1). 남은 시간 동안 제 크기로 돌아온다.</summary>
        private const float OvershootRatio = 0.6f;

        private const int MaximumStars = 3;

        private static Texture2D starTexture;
        private static Texture2D fillTexture;

        private AudioSource source;
        private AudioClip popClip;
        private GUIStyle titleStyle;
        private GUIStyle eyebrowStyle;
        private GUIStyle scoreStyle;
        private GUIStyle summaryStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle secondaryButtonStyle;

        /// <summary>결과 화면이 열린 시각. 아직 안 열렸으면 음수다.</summary>
        private float shownAt = -1f;

        private int awardedStars;

        /// <summary>소리를 이미 낸 별 개수. 같은 별에 두 번 소리 내지 않으려고 센다.</summary>
        private int soundedStars;

        /// <summary>지금 화면에 떠 있는 별 개수(연출이 끝난 것 기준). 테스트가 이 값을 본다.</summary>
        public int AwardedStars => awardedStars;
        public Font UiFont => bodyFont != null
            ? bodyFont
            : Resources.Load<Font>("Pretendard-Regular");

        public void Configure(GameFlow gameFlow, CargoTracker cargoTracker)
        {
            flow = gameFlow;
            tracker = cargoTracker;
        }

        public void SetFonts(Font title, Font body)
        {
            titleFont = title;
            bodyFont = body;
        }

        /// <summary>
        /// 별이 커지는 배율. 0 에서 <see cref="OvershootScale"/> 까지 부풀었다가 1 로 안착한다.
        /// </summary>
        /// <param name="progress">이 별의 진행도 0~1.</param>
        public static float PopScale(float progress)
        {
            float clamped = Mathf.Clamp01(progress);

            if (clamped < OvershootRatio)
            {
                // 커지는 구간. 끝에서 감속해 오버슛 꼭대기를 부드럽게 짚는다.
                float rise = clamped / OvershootRatio;
                return OvershootScale * (1f - (1f - rise) * (1f - rise));
            }

            // 되돌아오는 구간. 양 끝이 평평한 곡선이라 꼭대기에서 각지지 않는다.
            float settle = (clamped - OvershootRatio) / (1f - OvershootRatio);
            return Mathf.Lerp(OvershootScale, 1f, settle * settle * (3f - 2f * settle));
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;

            // UI 소리라 거리에 따라 줄어들면 안 된다.
            source.spatialBlend = 0f;
        }

        private void Update()
        {
            if (flow == null || flow.State != GameState.Result)
            {
                // 재시작하면 연출을 처음부터 다시 보여준다.
                shownAt = -1f;
                soundedStars = 0;
                return;
            }

            if (shownAt < 0f)
            {
                shownAt = Time.unscaledTime;
                awardedStars = tracker == null ? 0 : tracker.StarRating;
                soundedStars = 0;
            }

            PlayPendingStarSounds();
        }

        /// <summary>이번 프레임에 새로 나타난 별마다 소리를 한 번씩 낸다.</summary>
        private void PlayPendingStarSounds()
        {
            while (soundedStars < awardedStars && StarProgress(soundedStars) > 0f)
            {
                popClip ??= CreatePopClip();

                // 뒤로 갈수록 음을 올려 세 번이 한 마디처럼 들리게 한다.
                source.pitch = 1f + soundedStars * 0.14f;
                source.PlayOneShot(popClip);
                soundedStars++;
            }
        }

        /// <summary>별 하나의 연출 진행도 0~1. 아직 차례가 오지 않았으면 0 이다.</summary>
        private float StarProgress(int index)
        {
            if (shownAt < 0f)
            {
                return 0f;
            }

            // 물리 테스트가 Time.timeScale 을 올려 두고 돌기 때문에 실시간을 쓴다.
            float elapsed = Time.unscaledTime - shownAt - index * StarStaggerSeconds;
            return elapsed <= 0f ? 0f : Mathf.Clamp01(elapsed / StarGrowSeconds);
        }

        private void OnGUI()
        {
            if (flow == null || flow.State != GameState.Result)
            {
                return;
            }

            // 디버그 HUD 보다 앞에 그린다. 낮은 값이 앞이다.
            GUI.depth = -10;

            EnsureResources();

            // 도착 장면은 남기고, 배송 보고서를 읽을 만큼만 배경을 눌러 준다.
            GUI.color = new Color(0f, 0f, 0f, 0.16f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), fillTexture);
            GUI.color = Color.white;

            float panelWidth = Mathf.Clamp(Screen.width * 0.42f, 360f, 620f);
            float panelProgress = shownAt < 0f
                ? 0f
                : Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - shownAt) / 0.22f);
            float panelLeft = Mathf.Lerp(-panelWidth, 0f, panelProgress);
            float padding = Mathf.Clamp(panelWidth * 0.1f, 34f, 58f);
            float contentWidth = panelWidth - padding * 2f;
            float starSize = Mathf.Clamp(Screen.height * 0.085f, 42f, 92f);
            float titleSize = Mathf.Clamp(Screen.height * 0.052f, 28f, 52f);
            float bodySize = Mathf.Clamp(Screen.height * 0.026f, 15f, 26f);

            DrawFill(new Rect(panelLeft, 0f, panelWidth, Screen.height),
                new Color(0.055f, 0.075f, 0.095f, 0.96f));
            DrawFill(new Rect(panelLeft + panelWidth - 7f, 0f, 7f, Screen.height),
                new Color(0.95f, 0.71f, 0.2f));

            titleStyle.fontSize = Mathf.RoundToInt(titleSize);
            eyebrowStyle.fontSize = Mathf.RoundToInt(bodySize * 0.72f);
            scoreStyle.fontSize = Mathf.RoundToInt(titleSize * 1.7f);
            summaryStyle.fontSize = Mathf.RoundToInt(bodySize);
            primaryButtonStyle.fontSize = Mathf.RoundToInt(bodySize);
            secondaryButtonStyle.fontSize = Mathf.RoundToInt(bodySize * 0.9f);

            float left = panelLeft + padding;
            float y = Screen.height * 0.09f;
            GUI.Label(new Rect(left, y, contentWidth, bodySize * 1.5f),
                "DELIVERY REPORT", eyebrowStyle);

            y += bodySize * 1.7f;
            GUI.Label(new Rect(left, y, contentWidth, titleSize * 1.4f),
                DescribeOutcome(), titleStyle);

            y += titleSize * 1.65f;
            DrawStars(new Rect(left, y, contentWidth, starSize), starSize);

            y += starSize * 1.35f;
            GUI.Label(new Rect(left, y, contentWidth, titleSize * 1.9f),
                DescribeScore(), scoreStyle);

            y += titleSize * 1.85f;
            GUI.Label(new Rect(left, y, contentWidth, bodySize * 1.7f),
                DescribeCargo(), summaryStyle);

            DrawButtons(panelLeft, panelWidth, padding, bodySize);
        }

        /// <summary>
        /// 별 세 자리를 그린다. 못 받은 자리도 어둡게 남겨 둬야 "몇 개 중 몇 개"인지 읽힌다.
        /// 못 받은 별은 처음부터 제자리에 있고, 받은 별만 커지며 나타난다.
        /// </summary>
        private void DrawStars(Rect bounds, float size)
        {
            float gap = size * 0.28f;
            float totalWidth = MaximumStars * size + (MaximumStars - 1) * gap;
            float left = bounds.x + (bounds.width - totalWidth) * 0.5f;

            for (int index = 0; index < MaximumStars; index++)
            {
                var slot = new Rect(left + index * (size + gap), bounds.y, size, size);

                if (index >= awardedStars)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.14f);
                    GUI.DrawTexture(slot, starTexture);
                    GUI.color = Color.white;
                    continue;
                }

                float progress = StarProgress(index);
                if (progress <= 0f)
                {
                    continue;
                }

                // 가운데를 잡고 키운다. 왼쪽 위를 기준으로 키우면 별이 옆으로 흘러간다.
                Matrix4x4 original = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * PopScale(progress), slot.center);

                GUI.color = new Color(1f, 0.82f, 0.28f);
                GUI.DrawTexture(slot, starTexture);
                GUI.color = Color.white;

                GUI.matrix = original;
            }
        }

        private void DrawButtons(float panelLeft, float panelWidth, float padding, float bodySize)
        {
            float buttonWidth = panelWidth - padding * 2f;
            float buttonHeight = Mathf.Clamp(bodySize * 2.6f, 48f, 66f);
            float gap = bodySize * 0.55f;
            float left = panelLeft + padding;
            float top = Screen.height - padding - buttonHeight * 2f - gap;

            if (GUI.Button(new Rect(left, top, buttonWidth, buttonHeight),
                "다시 배송", primaryButtonStyle))
            {
                flow.Restart();
            }

            var second = new Rect(left, top + buttonHeight + gap, buttonWidth, buttonHeight);
            if (GUI.Button(second, "스테이지 선택", secondaryButtonStyle))
            {
                flow.ReturnToMainMenu();
            }
        }

        private string DescribeOutcome()
        {
            if (awardedStars >= MaximumStars)
            {
                return "완벽 배송";
            }

            return awardedStars == 0 ? "배송 실패" : "배송 완료";
        }

        private string DescribeScore()
        {
            return tracker == null ? "- / -" : $"{tracker.RemainingCount} / {tracker.TotalCount}";
        }

        private string DescribeCargo()
        {
            if (tracker == null)
            {
                return string.Empty;
            }

            if (tracker.RemainingCount == tracker.TotalCount)
            {
                return $"화물 {tracker.TotalCount}개 모두 배송";
            }

            return tracker.RemainingCount == 0
                ? "배송한 화물이 없습니다"
                : $"화물 {tracker.RemainingCount}개 배송 · {tracker.DroppedCount}개 분실";
        }

        private void EnsureResources()
        {
            fillTexture ??= CreateFillTexture();
            starTexture ??= CreateStarTexture();

            if (titleStyle != null)
            {
                return;
            }

            var white = new Color(0.96f, 0.96f, 0.96f);
            var dim = new Color(0.75f, 0.8f, 0.83f);
            var yellow = new Color(0.95f, 0.71f, 0.2f);

            titleStyle = new GUIStyle
            {
                font = titleFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = white },
            };
            eyebrowStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = yellow },
            };
            scoreStyle = new GUIStyle
            {
                font = titleFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = white },
            };
            summaryStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = dim },
            };
            primaryButtonStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    textColor = new Color(0.055f, 0.075f, 0.095f),
                    background = CreateSolidTexture(new Color(0.95f, 0.71f, 0.2f)),
                },
                hover =
                {
                    textColor = new Color(0.055f, 0.075f, 0.095f),
                    background = CreateSolidTexture(new Color(1f, 0.8f, 0.32f)),
                },
                active =
                {
                    textColor = new Color(0.055f, 0.075f, 0.095f),
                    background = CreateSolidTexture(new Color(0.84f, 0.61f, 0.14f)),
                },
            };
            secondaryButtonStyle = new GUIStyle
            {
                font = bodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    textColor = white,
                    background = CreateSolidTexture(new Color(0.12f, 0.17f, 0.21f)),
                },
                hover =
                {
                    textColor = white,
                    background = CreateSolidTexture(new Color(0.18f, 0.45f, 0.78f)),
                },
                active =
                {
                    textColor = white,
                    background = CreateSolidTexture(new Color(0.12f, 0.34f, 0.63f)),
                },
            };
        }

        private void DrawFill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, fillTexture);
            GUI.color = Color.white;
        }

        private static Texture2D CreateFillTexture()
        {
            return CreateSolidTexture(Color.white);
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 흰 오각별 텍스처를 만든다. 색은 그릴 때 입히므로 여기서는 모양만 낸다.
        /// 가장자리 계단을 없애려고 픽셀 하나를 여러 번 나눠 재고 그 비율을 알파로 쓴다.
        /// </summary>
        private static Texture2D CreateStarTexture()
        {
            const int Size = 256;
            const int PointCount = 5;

            // 오각별의 안쪽 반지름 비율. 이보다 크면 별이 통통해져 오각형처럼 보인다.
            const float InnerRatio = 0.42f;
            const int SuperSample = 3;

            Vector2[] outline = BuildStarOutline(Size, PointCount, InnerRatio);

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];
            const int SamplesPerPixel = SuperSample * SuperSample;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int inside = 0;
                    for (int sampleY = 0; sampleY < SuperSample; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < SuperSample; sampleX++)
                        {
                            var sample = new Vector2(
                                x + (sampleX + 0.5f) / SuperSample,
                                y + (sampleY + 0.5f) / SuperSample);
                            if (IsInsideOutline(sample, outline))
                            {
                                inside++;
                            }
                        }
                    }

                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(255 * inside / SamplesPerPixel));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>바깥 꼭짓점과 안쪽 골을 번갈아 찍어 별 윤곽을 만든다.</summary>
        private static Vector2[] BuildStarOutline(int size, int pointCount, float innerRatio)
        {
            var outline = new Vector2[pointCount * 2];
            float center = size * 0.5f;

            // 가장자리에 여유를 둔다. 꼭짓점이 텍스처 경계에 닿으면 잘려 보인다.
            float outerRadius = center - 2f;

            for (int index = 0; index < outline.Length; index++)
            {
                // 첫 꼭짓점이 위를 향하게 90도에서 시작한다. 텍스처는 아래가 0 이라 +y 가 위다.
                float angle = Mathf.PI * 0.5f + Mathf.PI * index / pointCount;
                float radius = index % 2 == 0 ? outerRadius : outerRadius * innerRatio;
                outline[index] = new Vector2(
                    center + Mathf.Cos(angle) * radius,
                    center + Mathf.Sin(angle) * radius);
            }

            return outline;
        }

        /// <summary>윤곽 안쪽인지 본다. 오른쪽으로 반직선을 그어 변을 지나친 횟수를 센다.</summary>
        private static bool IsInsideOutline(Vector2 point, Vector2[] outline)
        {
            bool inside = false;
            for (int current = 0, previous = outline.Length - 1;
                current < outline.Length;
                previous = current++)
            {
                Vector2 a = outline[current];
                Vector2 b = outline[previous];

                if (a.y > point.y != b.y > point.y
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// 별이 나올 때 쓰는 짧은 소리를 만든다. 음이 살짝 올라가며 잦아드는 "팝" 이다.
        /// 파일로 두지 않는 이유는 이 한 소리 때문에 바이너리 에셋을 늘릴 것 없어서다.
        /// </summary>
        private static AudioClip CreatePopClip()
        {
            const int SampleRate = 44100;
            const float Seconds = 0.12f;
            const float StartFrequency = 660f;
            const float EndFrequency = 990f;

            int sampleCount = Mathf.RoundToInt(SampleRate * Seconds);
            var data = new float[sampleCount];

            for (int index = 0; index < sampleCount; index++)
            {
                float time = index / (float)SampleRate;
                float ratio = time / Seconds;

                // 빠르게 잦아들어야 짧게 끊긴 소리로 들린다.
                float envelope = Mathf.Exp(-time * 26f);
                float frequency = Mathf.Lerp(StartFrequency, EndFrequency, ratio);
                data[index] = Mathf.Sin(2f * Mathf.PI * frequency * time) * envelope * 0.35f;
            }

            AudioClip clip = AudioClip.Create("StarPop", sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
