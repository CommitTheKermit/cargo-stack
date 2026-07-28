using System;
using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 하나의 게임플레이 Truck 루트에서 차체 시각물만 바꿔 비교한다.
    /// 주행, Rigidbody, 짐칸 콜라이더와 화물 좌표계는 이 컴포넌트가 건드리지 않는다.
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
        [SerializeField] private int activeIndex;

        private GUIStyle titleStyle;
        private GUIStyle activeStyle;

        public int CandidateCount => candidates?.Length ?? 0;
        public int ActiveIndex => activeIndex;
        public string ActiveCandidateName =>
            IsValidIndex(activeIndex) ? CandidateNames[activeIndex] : "선택 없음";

        private void Awake()
        {
            if (CandidateCount != CandidateNames.Length)
            {
                Debug.LogError($"[CargoStack] 트럭 후보는 {CandidateNames.Length}개여야 한다", this);
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

        public void Select(int index)
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "트럭 후보 인덱스가 범위를 벗어났다");
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                candidates[candidateIndex].SetActive(candidateIndex == index);
            }

            activeIndex = index;
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
            if (CandidateCount != CandidateNames.Length)
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
    }
}
