using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 메인 메뉴 배경으로 쓸 게임플레이 녹화 영상을 만든다.
    ///
    /// 트럭을 짐 실은 채로 경로를 따라 달리게 하고 관전(디오라마) 카메라로 훑어, 그 화면을
    /// <see cref="MediaEncoder"/> 로 mp4 에 담는다. ffmpeg 없이 에디터 내장 인코더만 쓴다.
    /// 에디터에서는 물리·Update 가 돌지 않으므로, 트럭 자세는 TruckMover.GetPoseAt 를 그대로
    /// 복제해 경로 위 거리로 직접 계산한다(값은 씬 빌더가 넣는 기본값과 같다).
    /// </summary>
    public static class MenuBackgroundRecorder
    {
        private const string StageScenePath = "Assets/Scenes/Prototype.unity";
        private const string OutputAsset = "Assets/Video/MenuBackground.mp4";
        private const string PreviewFrame = "/tmp/cargo-stack-preview/menu-bg-frame.png";

        private const int Width = 1280;
        private const int Height = 720;
        private const int FrameRate = 30;
        private const int FrameCount = 300; // 10초

        // TruckMover 의 기본값과 씬 빌더의 RideHeight 를 그대로 쓴다.
        private const float FrontAxleOffset = 1.75f;
        private const float RearAxleOffset = -1.75f;
        private const float RideHeight = 0.75f;

        [MenuItem("CargoStack/메뉴 배경 영상 녹화")]
        public static void Record()
        {
            EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Single);

            var mover = Object.FindFirstObjectByType<TruckMover>();
            var path = Object.FindFirstObjectByType<RoutePath>();
            var rig = Object.FindFirstObjectByType<DioramaCamera>();
            if (mover == null || path == null || rig == null)
            {
                Debug.LogError("[CargoStack] 배경 녹화에 필요한 트럭/경로/카메라를 찾지 못했다.");
                return;
            }

            var serialized = new SerializedObject(mover);
            float startDistance = serialized.FindProperty("startDistance").floatValue;
            float goalDistance = serialized.FindProperty("goalDistance").floatValue;

            LoadCargoOntoBed(mover.transform);

            Camera camera = rig.GetComponent<Camera>();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputAsset));
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewFrame));
            string absolutePath = Path.GetFullPath(OutputAsset);

            var attributes = new VideoTrackAttributes
            {
                frameRate = new MediaRational(FrameRate),
                width = (uint)Width,
                height = (uint)Height,
                includeAlpha = false,
            };

            var renderTexture = new RenderTexture(Width, Height, 24);
            var frame = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            bool previousEnabled = camera.enabled;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                camera.enabled = true;
                camera.targetTexture = renderTexture;

                using var encoder = new MediaEncoder(absolutePath, attributes);
                for (int i = 0; i < FrameCount; i++)
                {
                    float t = FrameCount > 1 ? (float)i / (FrameCount - 1) : 0f;

                    // 도착 직전에서 멈추지 않게 살짝 여유를 두고 훑는다.
                    float distance = Mathf.Lerp(startDistance, goalDistance - 1f, t);
                    PlaceTruck(mover.transform, path, distance);

                    // 완만하게 시점을 돌려 정적인 느낌을 없앤다.
                    float yaw = Mathf.Lerp(18f, 52f, Mathf.SmoothStep(0f, 1f, t));
                    rig.SetFraming(yaw, 26f, 18f);

                    camera.Render();

                    RenderTexture.active = renderTexture;
                    frame.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                    frame.Apply();
                    encoder.AddFrame(frame);

                    // 중간 프레임 한 장을 눈으로 확인할 수 있게 남긴다.
                    if (i == FrameCount / 2)
                    {
                        File.WriteAllBytes(PreviewFrame, frame.EncodeToPNG());
                    }
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.enabled = previousEnabled;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(renderTexture);
            }

            AssetDatabase.ImportAsset(OutputAsset, ImportAssetOptions.ForceUpdate);
            Debug.Log(
                $"[CargoStack] 메뉴 배경 영상 녹화 완료: {OutputAsset} "
                + $"({Width}x{Height}, {FrameCount}프레임 @ {FrameRate}fps). "
                + "메인 메뉴를 다시 만들면 배경으로 붙는다.");
        }

        /// <summary>짐 몇 개를 짐칸에 올려 트럭에 매달고, 나머지는 숨긴다.</summary>
        private static void LoadCargoOntoBed(Transform truck)
        {
            GameObject bedObject = GameObject.Find("BedAnchor");
            if (bedObject == null)
            {
                return;
            }

            Transform bed = bedObject.transform;
            Cargo[] all = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);
            Vector2[] spots =
            {
                new Vector2(-0.5f, -0.5f),
                new Vector2(0.5f, -0.5f),
                new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
            };

            int loaded = 0;
            foreach (Cargo cargo in all)
            {
                if (loaded < spots.Length)
                {
                    var collider = cargo.GetComponent<Collider>();
                    float halfHeight = collider != null ? collider.bounds.extents.y : 0.4f;
                    cargo.transform.SetParent(bed, false);
                    cargo.transform.localPosition = new Vector3(
                        spots[loaded].x, halfHeight + 0.02f, spots[loaded].y);
                    cargo.transform.localRotation = Quaternion.identity;
                    loaded++;
                }
                else
                {
                    cargo.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>TruckMover.GetPoseAt 와 같은 방식으로 경로 위 거리에 트럭을 올린다.</summary>
        private static void PlaceTruck(Transform truck, RoutePath path, float distance)
        {
            Vector3 front = path.PositionAt(distance + FrontAxleOffset);
            Vector3 rear = path.PositionAt(distance + RearAxleOffset);

            Vector3 heading = front - rear;
            if (heading.sqrMagnitude < 1e-6f)
            {
                heading = truck.right;
            }

            Quaternion rotation = Quaternion.LookRotation(heading.normalized, Vector3.up)
                * Quaternion.Euler(0f, -90f, 0f);
            Vector3 position = (front + rear) * 0.5f + rotation * Vector3.up * RideHeight;
            truck.SetPositionAndRotation(position, rotation);
        }
    }
}
