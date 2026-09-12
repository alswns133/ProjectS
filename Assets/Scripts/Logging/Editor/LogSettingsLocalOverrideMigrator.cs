#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ProjectS.Logging.Editor
{
    [InitializeOnLoad]
    public static class LogSettingsLocalOverrideMigrator
    {
        private const string SharedSettingsPath = "Assets/Resources/Logging/LogSettings.asset";
        private const string LocalSettingsPath = "Assets/Resources/Logging/LogSettings.local.asset";

        static LogSettingsLocalOverrideMigrator()
        {
            EditorApplication.delayCall += MigrateLegacySharedSettingsOnLoad;
        }

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
