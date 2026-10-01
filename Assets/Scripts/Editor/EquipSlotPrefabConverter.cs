using System.Collections.Generic;
using ProjectS.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 씬에 손으로 배치된 장비창 슬롯(<see cref="EquipSlotView"/>)들을 프리팹 하나의 인스턴스로 바꾸고,
    /// 등급 표시(<see cref="ItemSlotGradeView"/>)를 붙여 Frame·Light에 연결하는 1회성 변환 툴. (TH)
    ///
    /// 슬롯 5개가 구조는 같은데 각자 사본이라, 하나를 고쳐도 나머지가 따라오지 않았다("반복 요소를 손으로
    /// 복사하지 않는다" 규칙). 씬 YAML을 직접 고치지 않고 툴로 하는 이유는, 장비창(EquipmentPopup)이 슬롯들을
    /// 직렬화 참조로 들고 있어 오브젝트를 새로 만들면 연결이 끊기기 때문이다 — 여기서는 기존 오브젝트를
    /// "제자리에서" 프리팹 인스턴스로 전환해 참조와 부위(slot) 값·위치를 그대로 둔다.
    ///
    /// 사용: 장비창이 있는 씬(Bootstrap)을 연 뒤 메뉴 실행 → 결과 확인 후 씬 저장. 다시 실행해도 이미 전환된
    /// 슬롯은 건너뛴다.
    /// </summary>
    public static class EquipSlotPrefabConverter
    {
        private const string PrefabPath = "Assets/Prefabs/UI/Slot/EquipSlot.prefab";
        private const string FrameName = "Frame";
        private const string LightName = "Light";

        [MenuItem("Tools/ProjectS/Convert Equip Slots To Prefab")]
        private static void Convert()
        {
            var targets = new List<EquipSlotView>();
            foreach (EquipSlotView view in Object.FindObjectsByType<EquipSlotView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(view.gameObject)) targets.Add(view);
            }

            if (targets.Count == 0)
            {
                Debug.Log("[EquipSlotPrefabConverter] 전환할 슬롯이 없다(열린 씬에 EquipSlotView가 없거나 이미 프리팹 인스턴스).");
                return;
            }

            ItemGradeStyle style = FindStyle();
            if (style == null)
                Debug.LogWarning("[EquipSlotPrefabConverter] ItemGradeStyle 에셋을 찾지 못했다. 프리팹의 Style 칸을 직접 연결해야 한다.");

            // 전환 전에 모든 슬롯에 같은 구성을 먼저 붙인다 — 계층이 프리팹과 같아야 이름으로 짝이 맞는다.
            foreach (EquipSlotView view in targets)
                AttachGradeView(view.gameObject, style);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            int converted = 0;

            foreach (EquipSlotView view in targets)
            {
                GameObject go = view.gameObject;

                if (prefab == null)
                {
                    // 첫 슬롯이 프리팹의 원본이 된다(이 슬롯의 부위 값이 프리팹 기본값, 나머지는 인스턴스 오버라이드).
                    prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath, InteractionMode.AutomatedAction);
                }
                else
                {
                    var settings = new ConvertToPrefabInstanceSettings
                    {
                        changeRootNameToAssetName = false,          // Helmet/Weapon 같은 부위 이름 유지
                        recordPropertyOverridesOfMatches = true,    // 부위(slot)·위치 등 슬롯별로 다른 값 유지
                        gameObjectsNotMatchedBecomesOverride = true,
                        componentsNotMatchedBecomesOverride = true,
                    };
                    PrefabUtility.ConvertToPrefabInstance(go, prefab, settings, InteractionMode.AutomatedAction);
                }

                EditorSceneManager.MarkSceneDirty(go.scene);
                converted++;
            }

            Debug.Log($"[EquipSlotPrefabConverter] 슬롯 {converted}개를 {PrefabPath} 인스턴스로 전환했다. 확인 후 씬을 저장할 것.");
        }

        // ItemSlotGradeView를 붙이고, 슬롯 안의 외곽 라인(Frame)·내부 글로우(Light)를 이름으로 찾아 연결한다.
        // 해당 오브젝트가 없으면 칸을 비워 둔다(프리팹에서 직접 만들어 연결).
        private static void AttachGradeView(GameObject slot, ItemGradeStyle style)
        {
            ItemSlotGradeView gradeView = slot.GetComponent<ItemSlotGradeView>();
            if (gradeView == null) gradeView = slot.AddComponent<ItemSlotGradeView>();

            var so = new SerializedObject(gradeView);
            so.FindProperty("style").objectReferenceValue = style;
            so.FindProperty("frame").objectReferenceValue = FindImage(slot, FrameName);
            so.FindProperty("innerLight").objectReferenceValue = FindImage(slot, LightName);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 슬롯 하위(깊이 무관)에서 이름이 같은 오브젝트의 Image를 찾는다.
        private static Image FindImage(GameObject slot, string objectName)
        {
            foreach (Image image in slot.GetComponentsInChildren<Image>(true))
            {
                if (image.name == objectName) return image;
            }

            return null;
        }

        private static ItemGradeStyle FindStyle()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ItemGradeStyle)}");
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<ItemGradeStyle>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }
    }
}
