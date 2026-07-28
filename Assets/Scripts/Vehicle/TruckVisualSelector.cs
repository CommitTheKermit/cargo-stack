using System;
using UnityEngine;

namespace CargoStack
{
    [Serializable]
    public struct TruckBedProfile
    {
        [SerializeField] private float centerX;
        [SerializeField] private float centerZ;
        [SerializeField] private float floorTop;
        [SerializeField] private float insideLength;
        [SerializeField] private float insideWidth;
        [SerializeField] private float wallHeight;
        [SerializeField] private float floorThickness;
        [SerializeField] private float wallThickness;

        public TruckBedProfile(
            float centerX,
            float centerZ,
            float floorTop,
            float insideLength,
            float insideWidth,
            float wallHeight,
            float floorThickness = 0.12f,
            float wallThickness = 0.10f)
        {
            this.centerX = centerX;
            this.centerZ = centerZ;
            this.floorTop = floorTop;
            this.insideLength = insideLength;
            this.insideWidth = insideWidth;
            this.wallHeight = wallHeight;
            this.floorThickness = floorThickness;
            this.wallThickness = wallThickness;
        }

        public float CenterX => centerX;
        public float CenterZ => centerZ;
        public float FloorTop => floorTop;
        public float InsideLength => insideLength;
        public float InsideWidth => insideWidth;
        public float WallHeight => wallHeight;
        public float FloorThickness => floorThickness;
        public float WallThickness => wallThickness;
        public float MinX => centerX - insideLength * 0.5f;
        public float MaxX => centerX + insideLength * 0.5f;
        public float MinZ => centerZ - insideWidth * 0.5f;
        public float MaxZ => centerZ + insideWidth * 0.5f;
    }

    /// <summary>
    /// 하나의 게임플레이 Truck 루트에서 차체 시각물과 그 차체에 맞는 공유 짐칸 물리를 바꿔 비교한다.
    /// </summary>
    public sealed class TruckVisualSelector : MonoBehaviour
    {
        private static readonly string[] CandidateNames =
        {
            "카툰 트럭",
            "로우폴리 픽업",
            "무료 픽업",
        };

        [SerializeField] private GameObject[] candidates;
        [SerializeField] private TruckBedProfile[] bedProfiles;
        [SerializeField] private Transform bedAnchor;
        [SerializeField] private Transform bedFloor;
        [SerializeField] private Transform bedWallLeft;
        [SerializeField] private Transform bedWallRight;
        [SerializeField] private Transform bedWallRear;
        [SerializeField] private Transform bedWallFront;
        [SerializeField] private int activeIndex;

        private GUIStyle titleStyle;
        private GUIStyle activeStyle;
        private bool profileApplied;

        public int CandidateCount => candidates?.Length ?? 0;
        public int ActiveIndex => activeIndex;
        public string ActiveCandidateName =>
            IsValidIndex(activeIndex) ? CandidateNames[activeIndex] : "선택 없음";
        public TruckBedProfile ActiveProfile => GetBedProfile(activeIndex);

        private void Awake()
        {
            if (!HasCompleteConfiguration())
            {
                Debug.LogError("[CargoStack] 트럭 후보와 짐칸 프로필 연결이 완전하지 않다", this);
                return;
            }

            Select(Mathf.Clamp(activeIndex, 0, CandidateCount - 1));
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                SelectFromShortcut(KeyCode.Alpha1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SelectFromShortcut(KeyCode.Alpha2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SelectFromShortcut(KeyCode.Alpha3);
            }
        }

        public GameObject GetCandidate(int index)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "트럭 후보 인덱스가 범위를 벗어났다");
            }

            return candidates[index];
        }

        public string GetCandidateName(int index)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "트럭 후보 인덱스가 범위를 벗어났다");
            }

            return CandidateNames[index];
        }

        public TruckBedProfile GetBedProfile(int index)
        {
            if (!IsValidIndex(index) || bedProfiles == null || index >= bedProfiles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "트럭 짐칸 프로필 인덱스가 범위를 벗어났다");
            }

            return bedProfiles[index];
        }

        public void Configure(
            GameObject[] visualCandidates,
            TruckBedProfile[] profiles,
            Transform anchor,
            Transform floor,
            Transform leftWall,
            Transform rightWall,
            Transform rearWall,
            Transform frontWall)
        {
            candidates = visualCandidates;
            bedProfiles = profiles;
            bedAnchor = anchor;
            bedFloor = floor;
            bedWallLeft = leftWall;
            bedWallRight = rightWall;
            bedWallRear = rearWall;
            bedWallFront = frontWall;
        }

        public void Select(int index)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "트럭 후보 인덱스가 범위를 벗어났다");
            }

            TruckBedProfile previousProfile = profileApplied ? bedProfiles[activeIndex] : default;
            Vector3 previousAnchorPosition = bedAnchor.localPosition;

            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                candidates[candidateIndex].SetActive(candidateIndex == index);
            }

            activeIndex = index;
            ApplyBedProfile(bedProfiles[index]);

            if (profileApplied)
            {
                RebaseLoadedCargo(previousProfile, bedAnchor.localPosition - previousAnchorPosition);
            }

            profileApplied = true;
            Physics.SyncTransforms();
        }

        public void SelectFromButton(int index)
        {
            Select(index);
        }

        public bool SelectFromShortcut(KeyCode key)
        {
            int index = key switch
            {
                KeyCode.Alpha1 or KeyCode.Keypad1 => 0,
                KeyCode.Alpha2 or KeyCode.Keypad2 => 1,
                KeyCode.Alpha3 or KeyCode.Keypad3 => 2,
                _ => -1,
            };

            if (!IsValidIndex(index))
            {
                return false;
            }

            Select(index);
            return true;
        }

        private void OnGUI()
        {
            if (!HasCompleteConfiguration())
            {
                return;
            }

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
            };
            activeStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.55f, 1f, 0.55f) },
            };

            float panelWidth = 250f;
            GUILayout.BeginArea(
                new Rect(Screen.width - panelWidth - 16f, 16f, panelWidth, 190f),
                GUI.skin.box);

            GUILayout.Label("트럭 시각물 비교", titleStyle);
            GUILayout.Label($"현재: {ActiveCandidateName}", activeStyle);
            GUILayout.Space(6f);

            for (int index = 0; index < CandidateCount; index++)
            {
                if (GUILayout.Button($"{index + 1}. {CandidateNames[index]}", GUILayout.Height(30f)))
                {
                    SelectFromButton(index);
                }
            }

            GUILayout.EndArea();
        }

        private bool IsValidIndex(int index)
        {
            return candidates != null &&
                   index >= 0 &&
                   index < candidates.Length &&
                   index < CandidateNames.Length &&
                   candidates[index] != null;
        }

        private bool HasCompleteConfiguration()
        {
            return CandidateCount == CandidateNames.Length &&
                   bedProfiles != null &&
                   bedProfiles.Length == CandidateNames.Length &&
                   bedAnchor != null &&
                   bedFloor != null &&
                   bedWallLeft != null &&
                   bedWallRight != null &&
                   bedWallRear != null &&
                   bedWallFront != null;
        }

        private void ApplyBedProfile(TruckBedProfile profile)
        {
            float sideWallLength = profile.InsideLength + profile.WallThickness * 2f;
            float endWallWidth = profile.InsideWidth;
            float wallCenterY = profile.FloorTop + profile.WallHeight * 0.5f;

            bedAnchor.localPosition = new Vector3(profile.CenterX, profile.FloorTop, profile.CenterZ);
            SetPart(
                bedFloor,
                new Vector3(profile.CenterX, profile.FloorTop - profile.FloorThickness * 0.5f, profile.CenterZ),
                new Vector3(sideWallLength, profile.FloorThickness, profile.InsideWidth));
            SetPart(
                bedWallLeft,
                new Vector3(
                    profile.CenterX,
                    wallCenterY,
                    profile.MinZ - profile.WallThickness * 0.5f),
                new Vector3(sideWallLength, profile.WallHeight, profile.WallThickness));
            SetPart(
                bedWallRight,
                new Vector3(
                    profile.CenterX,
                    wallCenterY,
                    profile.MaxZ + profile.WallThickness * 0.5f),
                new Vector3(sideWallLength, profile.WallHeight, profile.WallThickness));
            SetPart(
                bedWallRear,
                new Vector3(
                    profile.MinX - profile.WallThickness * 0.5f,
                    wallCenterY,
                    profile.CenterZ),
                new Vector3(profile.WallThickness, profile.WallHeight, endWallWidth));
            SetPart(
                bedWallFront,
                new Vector3(
                    profile.MaxX + profile.WallThickness * 0.5f,
                    wallCenterY,
                    profile.CenterZ),
                new Vector3(profile.WallThickness, profile.WallHeight, endWallWidth));
        }

        private void RebaseLoadedCargo(TruckBedProfile previousProfile, Vector3 localAnchorDelta)
        {
            if (localAnchorDelta.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Vector3 worldDelta = transform.TransformVector(localAnchorDelta);
            foreach (Cargo cargo in FindObjectsByType<Cargo>(FindObjectsSortMode.None))
            {
                Vector3 localPosition = transform.InverseTransformPoint(cargo.Body.worldCenterOfMass);
                bool wasOnBed =
                    localPosition.x >= previousProfile.MinX - 0.15f &&
                    localPosition.x <= previousProfile.MaxX + 0.15f &&
                    localPosition.z >= previousProfile.MinZ - 0.15f &&
                    localPosition.z <= previousProfile.MaxZ + 0.15f &&
                    localPosition.y >= previousProfile.FloorTop - 0.2f &&
                    localPosition.y <= previousProfile.FloorTop + 3f;
                if (!wasOnBed)
                {
                    continue;
                }

                Vector3 targetPosition = cargo.Body.position + worldDelta;
                cargo.Body.position = targetPosition;
                cargo.transform.position = targetPosition;
            }
        }

        private static void SetPart(Transform part, Vector3 localPosition, Vector3 localScale)
        {
            part.localPosition = localPosition;
            part.localRotation = Quaternion.identity;
            part.localScale = localScale;
        }
    }
}
