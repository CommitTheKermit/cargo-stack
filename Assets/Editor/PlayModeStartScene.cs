using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CargoStack.EditorTools
{
    [InitializeOnLoad]
    public static class PlayModeStartScene
    {
        private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        private const string PreferenceKey = "CargoStack.PlayFromMainMenu";
        private const string MenuPath = "CargoStack/Play Mode/메인 메뉴부터 시작";

        static PlayModeStartScene()
        {
            EditorApplication.delayCall += ApplyPreference;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool enabled = !EditorPrefs.GetBool(PreferenceKey, true);
            EditorPrefs.SetBool(PreferenceKey, enabled);
            ApplyPreference();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(
                MenuPath,
                EditorPrefs.GetBool(PreferenceKey, true));
            return true;
        }

        public static void ApplyPreference()
        {
            if (Application.isBatchMode)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            bool enabled = EditorPrefs.GetBool(PreferenceKey, true);
            SceneAsset mainMenu = enabled
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath)
                : null;

            EditorSceneManager.playModeStartScene = mainMenu;
        }
    }
}
