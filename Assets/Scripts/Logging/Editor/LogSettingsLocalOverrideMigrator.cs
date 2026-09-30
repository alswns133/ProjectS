#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ProjectS.Logging.Editor
{
    /// <summary>공유 LogSettings 에셋에 들어간 URL·시크릿을 Git 제외 로컬 오버라이드(LogSettings.local.asset)로 옮기고 공유 에셋을 템플릿 기본값으로 비운다. 에디터 로드 시 자동으로도 한 번 검사한다.</summary>
    [InitializeOnLoad]
    public static class LogSettingsLocalOverrideMigrator
    {
        private const string SharedSettingsPath = "Assets/Resources/Logging/LogSettings.asset";
        private const string LocalSettingsPath = "Assets/Resources/Logging/LogSettings.local.asset";

        static LogSettingsLocalOverrideMigrator()
        {
            EditorApplication.delayCall += MigrateLegacySharedSettingsOnLoad;
        }

        /// <summary>메뉴에서 수동 실행. 로컬 오버라이드가 이미 있으면 아무것도 바꾸지 않는다.</summary>
        [MenuItem("Tools/ProjectS/Logging/Migrate Log Settings To Local Override")]
        public static void MigrateExistingSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<LogSettings>(LocalSettingsPath) != null)
            {
                Debug.LogWarning("[LogSettings] A local override already exists. No settings were changed.");
                return;
            }

            LogSettings sharedSettings = AssetDatabase.LoadAssetAtPath<LogSettings>(SharedSettingsPath);
            if (sharedSettings == null)
            {
                Debug.LogError("[LogSettings] The shared LogSettings template could not be found.");
                return;
            }

            CreateLocalOverride(sharedSettings);
        }

        private static void MigrateLegacySharedSettingsOnLoad()
        {
            if (AssetDatabase.LoadAssetAtPath<LogSettings>(LocalSettingsPath) != null)
            {
                return;
            }

            LogSettings sharedSettings = AssetDatabase.LoadAssetAtPath<LogSettings>(SharedSettingsPath);
            if (sharedSettings != null && sharedSettings.HasRemoteConfiguration)
            {
                CreateLocalOverride(sharedSettings);
            }
        }

        private static void CreateLocalOverride(LogSettings sharedSettings)
        {
            LogSettings localSettings = Object.Instantiate(sharedSettings);
            localSettings.name = "LogSettings.local";
            AssetDatabase.CreateAsset(localSettings, LocalSettingsPath);

            sharedSettings.ResetToTemplateDefaults();
            EditorUtility.SetDirty(localSettings);
            EditorUtility.SetDirty(sharedSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[LogSettings] Created a Git-ignored local override and reset the shared template.");
        }
    }
}
#endif
